import os
import sys
import json
import logging
import asyncio
import threading
import time
import re
import telebot
from telebot import types

BOT_TOKEN = "REDACTED_TELEGRAM_BOT_TOKEN"
ADMIN_IDS = [5237252950]
DONATE_URL = "https://www.donationalerts.com/r/toliyasdgg"

_DA_TOKEN_FILE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "da_tokens.json")
def _get_da_token():
    if os.path.exists(_DA_TOKEN_FILE):
        with open(_DA_TOKEN_FILE, "r") as f:
            return json.load(f).get("access_token", "")
    return ""
DONATIONALERTS_TOKEN = _get_da_token()

DATA_FILE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "bot_data.json")

PRICES = {
    "1day":    {"amount": 49,   "label": "1 Day",    "days": 1},
    "7days":   {"amount": 199,  "label": "7 Days",   "days": 7},
    "monthly": {"amount": 499,  "label": "1 Month",  "days": 30},
    "3months": {"amount": 999,  "label": "3 Months", "days": 90},
    "yearly":  {"amount": 1999, "label": "1 Year",   "days": 365},
}

AMOUNT_TO_TYPE = {v["amount"]: k for k, v in PRICES.items()}

logging.basicConfig(level=logging.INFO, format="%(asctime)s [%(levelname)s] %(message)s")
log = logging.getLogger(__name__)

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import license as L
from keygen import generate_key

def verify_key(key_str):
    try:
        return L.validate(key_str)
    except L.LicenseError:
        return None

SETUP_EXE_PATHS = [
    os.path.join(os.path.dirname(os.path.abspath(__file__)), "LichessBotSetup.zip"),
    os.path.join(os.path.dirname(os.path.abspath(__file__)), "LichessBotSetup.exe"),
    os.path.join(os.path.dirname(os.path.abspath(__file__)),
                 "LichessBotSetup", "bin", "Release", "net9.0-windows", "win-x64", "publish", "LichessBotSetup.exe"),
]

def _find_local_setup():
    for p in SETUP_EXE_PATHS:
        if os.path.exists(p):
            return p
    return None

def _send_setup(chat_id, version=None):
    fid = data.get("update_file_id")
    ver = version or data.get("update_version", "latest")

    if fid:
        bot.send_document(chat_id, fid, caption=f"Lichess Bot Setup v{ver}")
        return True

    local = _find_local_setup()
    if local:
        size_mb = os.path.getsize(local) / (1024 * 1024)
        if size_mb <= 49:
            with open(local, "rb") as f:
                fname = os.path.basename(local)
                result = bot.send_document(chat_id, f, caption=f"Lichess Bot Setup v{ver}",
                                            visible_file_name=fname)
                data["update_file_id"] = result.document.file_id
                data["update_version"] = ver
                _save_data(data)
            return True

    dl_url = data.get("download_url")
    if dl_url:
        kb = types.InlineKeyboardMarkup()
        kb.add(types.InlineKeyboardButton("📥 Download Setup", url=dl_url))
        bot.send_message(chat_id,
            f"📦 <b>Lichess Bot Setup v{ver}</b>\n\nClick below to download:",
            reply_markup=kb)
        return True

    bot.send_message(chat_id, "⚠️ No setup file available yet. Contact support.")
    return False

def _load_data():
    if os.path.exists(DATA_FILE):
        with open(DATA_FILE, "r", encoding="utf-8") as f:
            return json.load(f)
    return {"verified_users": {}, "update_file_id": None, "update_version": None,
            "admin_ids": ADMIN_IDS, "pending_payments": {}}

def _save_data(d):
    with open(DATA_FILE, "w", encoding="utf-8") as f:
        json.dump(d, f, indent=2, ensure_ascii=False)

data = _load_data()
if data.get("admin_ids"):
    ADMIN_IDS.clear()
    ADMIN_IDS.extend(data["admin_ids"])

bot = telebot.TeleBot(BOT_TOKEN, parse_mode="HTML")

def is_verified(cid): return str(cid) in data.get("verified_users", {})
def is_admin(cid): return cid in ADMIN_IDS

def main_kb(cid):
    kb = types.ReplyKeyboardMarkup(resize_keyboard=True)
    if is_verified(cid):
        kb.row("📥 Get Update", "🔄 Check Version")
        kb.row("ℹ️ My License", "🛒 Buy License")
        kb.row("💬 Support")
    else:
        kb.row("🛒 Buy License", "💬 Support")
    return kb

@bot.message_handler(commands=["start"])
def cmd_start(m):
    cid = m.chat.id

    args = m.text.split(maxsplit=1)
    if len(args) > 1:
        key = args[1].strip()
        info = verify_key(key)
        if info:
            data.setdefault("verified_users", {})[str(cid)] = {
                "key": key, "info": info,
                "username": m.from_user.username,
                "name": f"{m.from_user.first_name or ''} {m.from_user.last_name or ''}".strip()}
            _save_data(data)

            bot.send_message(cid,
                f"✅ <b>License verified!</b> ({info['type']})\n\n📥 Sending setup...",
                reply_markup=main_kb(cid))
            _send_setup(cid)
            return
        else:
            bot.send_message(cid, "❌ Invalid license key.", reply_markup=main_kb(cid))
            return

    if is_verified(cid):
        bot.send_message(cid, "👋 <b>Welcome back!</b>\n\nUse <b>📥 Get Update</b> to download.", reply_markup=main_kb(cid))
    else:
        banner = os.path.join(os.path.dirname(os.path.abspath(__file__)), "bot_banner.jpg")

        lines = [
            "♞ <b>Lichess Bot Controller</b>\n",
            "Automated chess bot for Lichess.org. "
            "Plays rated and casual games 24/7 using Stockfish engine "
            "with configurable skill level, time controls and opening books.\n",
            "🔹 Auto-accept challenges",
            "🔹 Adjustable engine strength",
            "🔹 Opening book support",
            "🔹 Auto-resign in lost positions",
            "🔹 One-click setup\n",
            "━━━━━━━━━━━━━━━━━━━━━━\n",
            "💳 <b>License Plans:</b>\n",
        ]
        for info in PRICES.values():
            lines.append(f"  • <b>{info['label']}</b> — {info['amount']} ₽")
        lines.append(
            f"\n<b>How to buy:</b>\n"
            f"1. Click <b>Pay</b> below\n"
            f"2. Send donation with exact amount\n"
            f"3. In the message write: <code>{cid}</code>\n"
            f"4. Key is sent here automatically!\n\n"
            f"Already have a key? Just send it here ⬇️")

        kb = types.InlineKeyboardMarkup()
        kb.add(types.InlineKeyboardButton("💳 Pay via DonationAlerts", url=DONATE_URL))

        if os.path.exists(banner):
            with open(banner, "rb") as f:
                bot.send_photo(cid, f, caption="\n".join(lines), reply_markup=kb)
        else:
            bot.send_message(cid, "\n".join(lines), reply_markup=kb)

        bot.send_message(cid, "⌨️", reply_markup=main_kb(cid))

@bot.message_handler(commands=["admin"])
def cmd_admin(m):
    cid = m.chat.id
    if not ADMIN_IDS:
        ADMIN_IDS.append(cid)
        data["admin_ids"] = ADMIN_IDS
        _save_data(data)
        bot.send_message(cid, f"✅ Admin registered. ID: <code>{cid}</code>")
        return
    if not is_admin(cid):
        bot.send_message(cid, "⛔ Access denied.")
        return
    bot.send_message(cid,
        f"📊 <b>Admin Panel</b>\n\n"
        f"Users: {len(data.get('verified_users', {}))}\n"
        f"Version: v{data.get('update_version', 'none')}\n"
        f"File: {'✅' if data.get('update_file_id') else '❌'}")

@bot.message_handler(commands=["release"])
def cmd_release(m):
    cid = m.chat.id
    if not is_admin(cid):
        bot.send_message(cid, "⛔ Access denied.")
        return
    bot.send_message(cid, "🚀 Starting release pipeline...")
    threading.Thread(target=_run_release, args=(cid,), daemon=True).start()

def _run_release(chat_id):
    try:
        import release as R
        R.ADMIN_IDS = ADMIN_IDS
        R.BOT_TOKEN = BOT_TOKEN
        success = R.full_release(notify_chat=chat_id)
        if success:
            global data
            data = _load_data()
    except Exception as e:
        bot.send_message(chat_id, f"❌ Release error: <pre>{e}</pre>", parse_mode="HTML")

@bot.message_handler(commands=["seturl"])
def cmd_seturl(m):
    cid = m.chat.id
    if not is_admin(cid):
        bot.send_message(cid, "⛔ Access denied.")
        return
    parts = m.text.split(maxsplit=1)
    if len(parts) < 2:
        bot.send_message(cid, "Usage: /seturl https://example.com/setup.exe")
        return
    url = parts[1].strip()
    data["download_url"] = url
    _save_data(data)
    bot.send_message(cid, f"✅ Download URL set:\n{url}")

@bot.message_handler(content_types=["document"])
def handle_doc(m):
    if not is_admin(m.chat.id):
        bot.send_message(m.chat.id, "Send your <b>license key</b> as text.")
        return
    doc = m.document
    version = None
    if m.caption:
        version = m.caption.strip().lstrip("v")
    else:
        match = re.search(r"v?(\d+\.\d+\.\d+)", doc.file_name or "", re.I)
        if match: version = match.group(1)
    if not version:
        bot.send_message(m.chat.id, "⚠️ Send version as caption (e.g. <code>1.5.0</code>)")
        return
    data["update_file_id"] = doc.file_id
    data["update_version"] = version
    _save_data(data)

    users = data.get("verified_users", {})
    sent, failed = 0, 0
    for cid_str in users:
        try:
            bot.send_message(int(cid_str),
                f"🆕 <b>Update v{version} available!</b>\n\n"
                f"Press <b>📥 Get Update</b> to download.",
                reply_markup=main_kb(int(cid_str)))
            sent += 1
        except Exception:
            failed += 1

    bot.send_message(m.chat.id,
        f"✅ <b>Update v{version} saved!</b> ({doc.file_name})\n"
        f"Notified: {sent} users, failed: {failed}")

@bot.message_handler(func=lambda m: m.text == "🛒 Buy License")
def btn_buy(m):
    cid = m.chat.id
    lines = ["💳 <b>License Plans:</b>\n"]
    for info in PRICES.values():
        lines.append(f"• <b>{info['label']}</b> — {info['amount']} ₽")
    lines.append(
        f"\n<b>How to buy:</b>\n"
        f"1. Click the button below\n"
        f"2. Send donation with <b>exact amount</b>\n"
        f"3. In the message write: <code>{cid}</code>\n"
        f"4. Key will be sent here automatically!\n\n"
        f"⚠️ <i>Amount must match exactly!</i>")

    kb = types.InlineKeyboardMarkup()
    kb.add(types.InlineKeyboardButton("💳 Pay via DonationAlerts", url=DONATE_URL))
    bot.send_message(cid, "\n".join(lines), reply_markup=kb)

    data.setdefault("pending_payments", {})[str(cid)] = {
        "time": time.time(), "username": m.from_user.username}
    _save_data(data)

@bot.message_handler(func=lambda m: m.text == "📥 Get Update")
def btn_update(m):
    cid = m.chat.id
    if not is_verified(cid):
        bot.send_message(cid, "⛔ Verify your key first.", reply_markup=main_kb(cid))
        return
    bot.send_message(cid, "📥 Sending setup...")
    _send_setup(cid)

@bot.message_handler(func=lambda m: m.text == "ℹ️ My License")
def btn_license(m):
    cid = m.chat.id
    if not is_verified(cid):
        bot.send_message(cid, "⛔ No license. Send your key to verify.")
        return
    ud = data["verified_users"].get(str(cid), {})
    info = ud.get("info", {})
    bot.send_message(cid,
        f"🔑 <b>Your License</b>\n\n"
        f"Type: {info.get('type', '?')}\nExpires: {info.get('expiry', '?')}\n"
        f"Days left: {info.get('days_left', '?')}\n"
        f"Key: <tg-spoiler>{ud.get('key', '?')}</tg-spoiler>")

@bot.message_handler(func=lambda m: m.text == "💬 Support")
def btn_support(m):
    bot.send_message(m.chat.id, "💬 <b>Support</b>\n\nDescribe your issue:")
    bot.register_next_step_handler(m, _handle_support)

def _handle_support(m):
    if not m.text or m.text.startswith("/"):
        bot.send_message(m.chat.id, "Cancelled.", reply_markup=main_kb(m.chat.id))
        return
    user = m.from_user
    name = f"{user.first_name or ''} {user.last_name or ''}".strip()
    uname = f"@{user.username}" if user.username else "no username"
    for aid in ADMIN_IDS:
        try:
            bot.send_message(aid,
                f"💬 <b>Support</b>\nFrom: {name} ({uname}, ID: <code>{m.chat.id}</code>)\n"
                f"Verified: {'✅' if is_verified(m.chat.id) else '❌'}\n\n{m.text}")
        except Exception: pass
    bot.send_message(m.chat.id, "✅ Sent to support!", reply_markup=main_kb(m.chat.id))

@bot.message_handler(func=lambda m: m.text == "🔄 Check Version")
def btn_check_version(m):
    cid = m.chat.id
    if not is_verified(cid):
        bot.send_message(cid, "⛔ Verify your key first.", reply_markup=main_kb(cid))
        return

    ver = data.get("update_version")
    fid = data.get("update_file_id")

    if not ver:
        bot.send_message(cid, "No version info available yet.", reply_markup=main_kb(cid))
        return

    kb = types.InlineKeyboardMarkup()
    if fid:
        kb.add(types.InlineKeyboardButton("📥 Download", callback_data="dl_update"))

    bot.send_message(cid,
        f"🔄 <b>Latest version: v{ver}</b>\n\n"
        f"Check the version on your PC (shown in the app title bar).\n"
        f"If it doesn't match — download the update below.",
        reply_markup=kb)

@bot.callback_query_handler(func=lambda c: c.data == "dl_update")
def cb_download(c):
    cid = c.message.chat.id
    if not is_verified(cid):
        bot.answer_callback_query(c.id, "Verify your key first.")
        return
    bot.answer_callback_query(c.id)
    _send_setup(cid)

@bot.message_handler(func=lambda m: True)
def handle_text(m):
    cid = m.chat.id
    text = (m.text or "").strip()
    if not text: return

    cleaned = text.upper().replace("-", "").replace(" ", "")
    if len(cleaned) < 20 or len(cleaned) > 50:
        bot.send_message(cid, "❓ Send a <b>license key</b> or use buttons.", reply_markup=main_kb(cid))
        return

    bot.send_message(cid, "🔄 Checking...")
    info = verify_key(text)
    if not info:
        bot.send_message(cid, "❌ <b>Invalid key.</b>", reply_markup=main_kb(cid))
        return

    data.setdefault("verified_users", {})[str(cid)] = {
        "key": text, "info": info,
        "username": m.from_user.username,
        "name": f"{m.from_user.first_name or ''} {m.from_user.last_name or ''}".strip()}
    _save_data(data)

    bot.send_message(cid,
        f"✅ <b>Verified!</b> ({info['type']})\n\n📥 Sending setup...",
        reply_markup=main_kb(cid))
    _send_setup(cid)

    for aid in ADMIN_IDS:
        try:
            bot.send_message(aid,
                f"🆕 Verified: {m.from_user.first_name} (@{m.from_user.username or 'none'}, {cid})\n"
                f"License: {info['type']} — {info['expiry']}")
        except Exception: pass

async def listen_donations():
    import httpx
    import websockets

    api = "https://www.donationalerts.com/api/v1"
    ws_url = "wss://centrifugo.donationalerts.com/connection/websocket"
    headers = {"Authorization": f"Bearer {DONATIONALERTS_TOKEN}"}

    while True:
        try:
            async with httpx.AsyncClient() as client:
                r = await client.get(f"{api}/user/oauth", headers=headers)
                r.raise_for_status()
                ud = r.json()["data"]

            user_id = ud["id"]
            socket_token = ud["socket_connection_token"]
            log.info(f"DonationAlerts connected (user_id: {user_id})")

            async with websockets.connect(ws_url) as ws:
                await ws.send(json.dumps({"params": {"token": socket_token}, "id": 1}))
                auth = json.loads(await ws.recv())
                client_id = auth["result"]["client"]

                channel = f"$alerts:donation_{user_id}"
                async with httpx.AsyncClient() as client:
                    r = await client.post(f"{api}/centrifuge/subscribe", headers=headers,
                        json={"client": client_id, "channels": [channel]})
                    r.raise_for_status()
                    sub_token = r.json()["channels"][0]["token"]

                await ws.send(json.dumps({"id": 2, "method": 1,
                    "params": {"channel": channel, "token": sub_token}}))
                await ws.recv()
                log.info("DonationAlerts: listening for donations...")

                async for raw in ws:
                    try:
                        msg = json.loads(raw)
                        if "id" in msg: continue
                        donation = msg.get("result", {}).get("data", {}).get("data")
                        if donation:
                            _process_donation(donation)
                    except Exception as e:
                        log.error(f"Donation error: {e}")

        except Exception as e:
            log.error(f"DonationAlerts error: {e}, reconnecting in 10s...")
            await asyncio.sleep(10)

def _process_donation(d):
    username = d.get("username") or d.get("name") or "Anonymous"
    amount = int(round(float(d.get("amount", 0))))
    currency = d.get("currency", "RUB")
    msg = d.get("message", "")

    log.info(f"[DONATE] {username}: {amount} {currency} — {msg}")

    key_type = AMOUNT_TO_TYPE.get(amount)
    if not key_type:
        for pa, kt in AMOUNT_TO_TYPE.items():
            if abs(amount - pa) <= 5:
                key_type = kt
                break

    if not key_type:
        for aid in ADMIN_IDS:
            try:
                bot.send_message(aid,
                    f"⚠️ <b>Unknown donation amount</b>\n\n"
                    f"From: {username}\nAmount: {amount} {currency}\nMessage: {msg}\n\n"
                    f"Issue key manually if needed.")
            except Exception: pass
        return

    chat_id = _extract_chat_id(msg)
    if not chat_id:
        for aid in ADMIN_IDS:
            try:
                bot.send_message(aid,
                    f"⚠️ <b>User not identified</b>\n\n"
                    f"From: {username}\nAmount: {amount} {currency} → {PRICES[key_type]['label']}\n"
                    f"Message: {msg}\n\nGenerate manually: <code>python keygen.py -t {key_type}</code>")
            except Exception: pass
        return

    try:
        key = generate_key(key_type)
        info = L.validate(key)
        bot.send_message(chat_id,
            f"🎉 <b>Payment received!</b>\n\n"
            f"Your key:\n<code>{key}</code>\n\n"
            f"Type: {info['type']}\nExpires: {info['expiry']}\nDays: {info['days_left']}\n\n"
            f"Paste this key in the bot app to activate.",
            reply_markup=main_kb(chat_id))

        data.setdefault("verified_users", {})[str(chat_id)] = {
            "key": key, "info": info, "username": username}
        _save_data(data)

        for aid in ADMIN_IDS:
            try:
                bot.send_message(aid,
                    f"💰 <b>Auto-sale!</b>\nUser: {username} ({chat_id})\n"
                    f"Plan: {PRICES[key_type]['label']} ({amount} {currency}) ✅")
            except Exception: pass
        log.info(f"[SALE] Key sent to {chat_id}: {key_type}")

    except Exception as e:
        for aid in ADMIN_IDS:
            try:
                bot.send_message(aid,
                    f"❌ <b>Key generation failed!</b>\n"
                    f"User: {username} ({chat_id})\nPlan: {PRICES[key_type]['label']}\nError: {e}\n\n"
                    f"Issue key manually!")
            except Exception: pass

def _extract_chat_id(msg):
    m = re.search(r"(?:id[:\s]*)?(\d{6,12})", msg, re.I)
    if m: return int(m.group(1))
    m = re.search(r"@(\w+)", msg)
    if m:
        uname = m.group(1).lower()
        for cid, pd in data.get("pending_payments", {}).items():
            if (pd.get("username") or "").lower() == uname:
                return int(cid)
    if msg.strip().isdigit() and len(msg.strip()) >= 6:
        return int(msg.strip())
    return None

def _run_da():
    loop = asyncio.new_event_loop()
    asyncio.set_event_loop(loop)
    loop.run_until_complete(listen_donations())

if __name__ == "__main__":
    log.info("Lichess Bot starting...")
    log.info(f"Admins: {ADMIN_IDS}")

    threading.Thread(target=_run_da, daemon=True).start()
    log.info("DonationAlerts listener started")

    bot.infinity_polling(timeout=30, long_polling_timeout=30)
