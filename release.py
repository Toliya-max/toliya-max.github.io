import os
import sys
import json
import subprocess
import requests

ROOT = os.path.dirname(os.path.abspath(__file__))
DATA_FILE = os.path.join(ROOT, "bot_data.json")
VERSION_FILE = os.path.join(ROOT, "version.txt")
DIST_EXE = os.path.join(ROOT, "dist", "LichessBotSetup.exe")
BUILD_SCRIPT = os.path.join(ROOT, "build_setup.ps1")

BOT_TOKEN = "REDACTED_TELEGRAM_BOT_TOKEN"
ADMIN_IDS = [5237252950]
TG_API = f"https://api.telegram.org/bot{BOT_TOKEN}"
MAX_TG_SIZE = 49 * 1024 * 1024


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


def tg_send(chat_id, text):
    requests.post(f"{TG_API}/sendMessage", json={
        "chat_id": chat_id, "text": text, "parse_mode": "HTML"})


def tg_upload(chat_id, file_path, caption=""):
    with open(file_path, "rb") as f:
        r = requests.post(f"{TG_API}/sendDocument",
                          data={"chat_id": chat_id, "caption": caption, "parse_mode": "HTML"},
                          files={"document": (os.path.basename(file_path), f)})
    r.raise_for_status()
    return r.json()["result"]["document"]["file_id"]


def build(notify_chat=None):
    if notify_chat:
        tg_send(notify_chat, "🔨 Building LichessBotSetup...")

    result = subprocess.run(
        ["powershell", "-ExecutionPolicy", "Bypass", "-File", BUILD_SCRIPT],
        cwd=ROOT, capture_output=True, text=True, timeout=300)

    if result.returncode != 0:
        err = result.stderr[-500:] if result.stderr else "unknown error"
        if notify_chat:
            tg_send(notify_chat, f"❌ Build failed:\n<pre>{err}</pre>")
        print(f"BUILD FAILED:\n{result.stderr}", file=sys.stderr)
        return False

    if not os.path.exists(DIST_EXE):
        if notify_chat:
            tg_send(notify_chat, "❌ Build produced no output.")
        return False

    size_mb = os.path.getsize(DIST_EXE) / (1024 * 1024)
    if notify_chat:
        tg_send(notify_chat, f"✅ Build OK — {size_mb:.1f} MB")
    print(f"Build OK: {DIST_EXE} ({size_mb:.1f} MB)")
    return True


def upload(notify_chat=None):
    version = get_version()
    data = load_data()
    size = os.path.getsize(DIST_EXE)

    if size < MAX_TG_SIZE:
        if notify_chat:
            tg_send(notify_chat, "📤 Uploading to Telegram...")
        file_id = tg_upload(notify_chat or ADMIN_IDS[0], DIST_EXE,
                            caption=f"Lichess Bot Setup v{version}")
        data["update_file_id"] = file_id
        data["update_version"] = version
        save_data(data)
        if notify_chat:
            tg_send(notify_chat, f"✅ Uploaded! file_id cached.")
        print(f"Uploaded. file_id: {file_id}")
    else:
        size_mb = size / (1024 * 1024)
        msg = (f"⚠️ Exe is {size_mb:.0f} MB (Telegram limit 50 MB).\n\n"
               f"Send the file manually to this chat to cache it, "
               f"or use /seturl to set a download link.")
        if notify_chat:
            tg_send(notify_chat, msg)
        data["update_version"] = version
        save_data(data)
        print(f"Too large for Telegram ({size_mb:.0f} MB). Manual upload needed.")


def notify_users(notify_chat=None):
    data = load_data()
    version = data.get("update_version", "?")
    users = data.get("verified_users", {})
    sent, failed = 0, 0

    for cid_str in users:
        cid = int(cid_str)
        if cid in ADMIN_IDS:
            continue
        try:
            tg_send(cid, f"🆕 <b>Update v{version} available!</b>\n\n"
                         f"Press <b>📥 Get Update</b> to download.")
            sent += 1
        except Exception:
            failed += 1

    msg = f"📢 Notified {sent} users (failed: {failed})"
    if notify_chat:
        tg_send(notify_chat, msg)
    print(msg)


def full_release(notify_chat=None):
    if not build(notify_chat):
        return False
    upload(notify_chat)
    notify_users(notify_chat)
    if notify_chat:
        tg_send(notify_chat, "🎉 <b>Release complete!</b>")
    return True


if __name__ == "__main__":
    admin = ADMIN_IDS[0] if "--notify" in sys.argv else None
    success = full_release(notify_chat=admin)
    sys.exit(0 if success else 1)
