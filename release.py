import os
import sys
import json
import subprocess
import asyncio
import requests
from dotenv import load_dotenv

ROOT = os.path.dirname(os.path.abspath(__file__))
load_dotenv(os.path.join(ROOT, ".env"))

DATA_FILE = os.path.join(ROOT, "bot_data.json")
VERSION_FILE = os.path.join(ROOT, "version.txt")
DIST_FILE = os.path.join(ROOT, "dist", "LichessBotSetup.zip")
BUILD_SCRIPT = os.path.join(ROOT, "build_setup.ps1")

BOT_TOKEN = os.environ.get("TELEGRAM_BOT_TOKEN", "").strip()
if not BOT_TOKEN:
    raise RuntimeError("TELEGRAM_BOT_TOKEN is not set in .env")
ADMIN_IDS = [int(x) for x in os.environ.get("TELEGRAM_ADMIN_IDS", "").split(",") if x.strip().isdigit()]
if not ADMIN_IDS:
    raise RuntimeError("TELEGRAM_ADMIN_IDS is not set in .env")
TG_API = f"https://api.telegram.org/bot{BOT_TOKEN}"
MAX_TG_SIZE = 49 * 1024 * 1024

TG_SESSION = os.path.join(ROOT, "tg_bot.session")


def load_data():
    if os.path.exists(DATA_FILE):
        with open(DATA_FILE, "r", encoding="utf-8") as f:
            return json.load(f)
    return {}


def save_data(d):
    with open(DATA_FILE, "w", encoding="utf-8") as f:
        json.dump(d, f, indent=2, ensure_ascii=False)


def get_version():
    if os.path.exists(VERSION_FILE):
        return open(VERSION_FILE).read().strip()
    return "unknown"


def _git(*args):
    try:
        r = subprocess.run(["git", *args], cwd=ROOT, capture_output=True,
                           text=True, encoding="utf-8", timeout=15)
        return r.stdout.strip() if r.returncode == 0 else ""
    except Exception:
        return ""


CHANGELOG_SKIP_PATTERNS = (
    "bump ", "merge ", "release ", "tag ",
)


def get_changelog(version, max_chars=600, max_items=10):
    prev_tag = _git("describe", "--tags", "--abbrev=0", f"v{version}^") or \
               _git("describe", "--tags", "--abbrev=0", "HEAD^")
    rng = f"{prev_tag}..HEAD" if prev_tag else "-20"
    raw = _git("log", rng, "--pretty=format:%s") if prev_tag else \
          _git("log", "-20", "--pretty=format:%s")
    if not raw:
        return ""
    lines = []
    for line in raw.splitlines():
        s = line.strip()
        if not s:
            continue
        low = s.lower()
        if any(low.startswith(p) for p in CHANGELOG_SKIP_PATTERNS):
            continue
        lines.append(s)
        if len(lines) >= max_items:
            break
    if not lines:
        return ""
    bullets = "\n".join(f"• {l}" for l in lines)
    if len(bullets) > max_chars:
        bullets = bullets[:max_chars - 3].rstrip() + "..."
    return bullets


def create_tag(version):
    tag = f"v{version}"
    if _git("rev-parse", "-q", "--verify", f"refs/tags/{tag}"):
        print(f"Tag {tag} already exists")
        return True
    r = subprocess.run(["git", "tag", tag], cwd=ROOT,
                       capture_output=True, text=True, timeout=15)
    if r.returncode != 0:
        print(f"git tag failed: {r.stderr.strip()}")
        return False
    r = subprocess.run(["git", "push", "origin", tag], cwd=ROOT,
                       capture_output=True, text=True, timeout=60)
    if r.returncode != 0:
        print(f"git push tag failed: {r.stderr.strip()}")
        return False
    print(f"Tagged and pushed {tag}")
    return True


def tg_send(chat_id, text):
    requests.post(f"{TG_API}/sendMessage", json={
        "chat_id": chat_id, "text": text, "parse_mode": "HTML"})


def tg_delete_message(chat_id, message_id):
    if not chat_id or not message_id:
        return
    try:
        requests.post(f"{TG_API}/deleteMessage",
                      json={"chat_id": chat_id, "message_id": message_id},
                      timeout=10)
    except Exception:
        pass


def tg_upload(chat_id, file_path, caption=""):
    with open(file_path, "rb") as f:
        r = requests.post(f"{TG_API}/sendDocument",
                          data={"chat_id": chat_id, "caption": caption, "parse_mode": "HTML"},
                          files={"document": (os.path.basename(file_path), f)})
    r.raise_for_status()
    result = r.json()["result"]
    return result["document"]["file_id"], result.get("message_id")


def tg_send_url(chat_id, url, caption=""):
    try:
        r = requests.post(f"{TG_API}/sendDocument", json={
            "chat_id": chat_id, "document": url,
            "caption": caption, "parse_mode": "HTML"}, timeout=300)
        data = r.json()
        if data.get("ok") and data["result"].get("document"):
            return data["result"]["document"]["file_id"]
        print(f"URL send failed: {data.get('description', 'unknown')}")
    except Exception as e:
        print(f"URL send error: {e}")
    return None


def tg_upload_stream(chat_id, file_path, caption=""):
    try:
        with open(file_path, "rb") as f:
            r = requests.post(
                f"{TG_API}/sendDocument",
                data={"chat_id": str(chat_id), "caption": caption, "parse_mode": "HTML"},
                files={"document": (os.path.basename(file_path), f, "application/octet-stream")},
                timeout=600)
        data = r.json()
        if data.get("ok") and data["result"].get("document"):
            return data["result"]["document"]["file_id"], data["result"].get("message_id")
        print(f"Stream upload failed: {data.get('description', 'unknown')}")
    except Exception as e:
        print(f"Stream upload error: {e}")
    return None, None


def tg_upload_large(chat_id, file_path, caption=""):
    from telethon import TelegramClient
    from telethon.sessions import StringSession

    api_id = 6
    api_hash = "eb06d4abfb49dc3eeb1aeb98ae0f581e"

    async def _upload():
        client = TelegramClient(TG_SESSION, api_id, api_hash)
        await client.start(bot_token=BOT_TOKEN)
        result = await client.send_file(
            chat_id, file_path,
            caption=caption,
            parse_mode="html",
            force_document=True)
        file_id = None
        if result.document:
            r = requests.post(f"{TG_API}/getFile",
                              json={"file_id": str(result.document.id)})
        await client.disconnect()
        return result

    loop = asyncio.new_event_loop()
    msg = loop.run_until_complete(_upload())
    loop.close()

    bot_msg = requests.post(f"{TG_API}/forwardMessage", json={
        "chat_id": chat_id, "from_chat_id": chat_id,
        "message_id": msg.id}).json()

    if bot_msg.get("ok") and bot_msg["result"].get("document"):
        return bot_msg["result"]["document"]["file_id"]

    return None


def build(notify_chat=None):
    result = subprocess.run(
        ["powershell", "-ExecutionPolicy", "Bypass", "-File", BUILD_SCRIPT],
        cwd=ROOT, capture_output=True, text=True, timeout=300)

    if result.returncode != 0:
        err = result.stderr[-500:] if result.stderr else "unknown error"
        if notify_chat:
            tg_send(notify_chat, f"❌ Build failed:\n<pre>{err}</pre>")
        print(f"BUILD FAILED:\n{result.stderr}", file=sys.stderr)
        return False

    if not os.path.exists(DIST_FILE):
        return False

    size_mb = os.path.getsize(DIST_FILE) / (1024 * 1024)
    print(f"Build OK: {DIST_FILE} ({size_mb:.1f} MB)")
    return True


def upload(notify_chat=None):
    version = get_version()
    data = load_data()
    size = os.path.getsize(DIST_FILE)

    data["update_version"] = version
    data.pop("download_url", None)

    changelog = get_changelog(version)
    if changelog:
        data["update_changelog"] = changelog
    else:
        data.pop("update_changelog", None)

    target = notify_chat or ADMIN_IDS[0]

    prev_chat = data.get("update_chat_id")
    prev_msg = data.get("update_message_id")
    if prev_chat and prev_msg:
        tg_delete_message(prev_chat, prev_msg)

    save_data(data)

    caption = f"<b>Lichess Bot Setup v{version}</b>"
    if changelog:
        caption += f"\n\n<b>What's new:</b>\n{changelog}"
    file_id = None
    msg_id = None

    if size < MAX_TG_SIZE:
        file_id, msg_id = tg_upload(target, DIST_FILE, caption=caption)

    if not file_id:
        file_id, msg_id = tg_upload_stream(target, DIST_FILE, caption=caption)

    if file_id:
        data["update_file_id"] = file_id
        data["update_message_id"] = msg_id
        data["update_chat_id"] = target
        save_data(data)
        print(f"Uploaded. file_id: {file_id}")
    else:
        print("Upload failed")


def tg_send_doc_by_id(chat_id, file_id, caption):
    try:
        r = requests.post(f"{TG_API}/sendDocument", json={
            "chat_id": chat_id,
            "document": file_id,
            "caption": caption,
            "parse_mode": "HTML",
        }, timeout=30)
        return r.ok and r.json().get("ok")
    except Exception:
        return False


def notify_users(notify_chat=None):
    data = load_data()
    version = data.get("update_version", "?")
    changelog = data.get("update_changelog", "") or ""
    file_id = data.get("update_file_id")
    users = data.get("verified_users", {})
    sent, failed, skipped = 0, 0, 0

    caption = f"🆕 <b>Update v{version} available!</b>"
    if changelog:
        caption += f"\n\n<b>What's new:</b>\n{changelog}"
    caption += ("\n\nExtract the ZIP and run <b>LichessBotSetup.exe</b> "
                "to upgrade. Your license and settings are preserved.")
    if len(caption) > 1020:
        caption = caption[:1017] + "..."

    fallback_text = (f"🆕 <b>Update v{version} available!</b>\n\n"
                     f"Press <b>📥 Get Update</b> to download.")

    fallback_count = 0
    for cid_str in users:
        try:
            cid = int(cid_str)
        except ValueError:
            skipped += 1
            continue
        if cid in ADMIN_IDS:
            skipped += 1
            continue
        try:
            delivered = False
            if file_id:
                delivered = tg_send_doc_by_id(cid, file_id, caption)
            if delivered:
                sent += 1
                continue
            try:
                r = requests.post(f"{TG_API}/sendMessage", json={
                    "chat_id": cid, "text": fallback_text, "parse_mode": "HTML",
                }, timeout=30)
                if r.ok and r.json().get("ok"):
                    fallback_count += 1
                    sent += 1
                else:
                    failed += 1
                    print(f"  user {cid}: send failed {r.status_code} {r.text[:200]}")
            except Exception as e:
                failed += 1
                print(f"  user {cid}: fallback failed {e}")
        except Exception as e:
            failed += 1
            print(f"  user {cid_str}: {e}")
    if fallback_count:
        msg = f"📢 Notified {sent} users (failed: {failed}, fallback-text: {fallback_count})"
    else:
        msg = f"📢 Notified {sent} users (failed: {failed})"
    if notify_chat:
        tg_send(notify_chat, msg)
    print(msg)


def full_release(notify_chat=None):
    version = get_version()
    create_tag(version)
    if not build(notify_chat):
        return False
    upload(notify_chat)
    notify_users(notify_chat)
    return True


if __name__ == "__main__":
    admin = ADMIN_IDS[0] if "--notify" in sys.argv else None
    success = full_release(notify_chat=admin)
    sys.exit(0 if success else 1)
