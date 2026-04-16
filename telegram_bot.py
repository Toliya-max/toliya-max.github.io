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
_DA_CLIENT_ID = "18598"
_DA_CLIENT_SECRET = "REDACTED_DA_CLIENT_SECRET"
_DA_SCOPE = "oauth-donation-subscribe oauth-donation-index oauth-user-show"

def _get_da_token():
    if os.path.exists(_DA_TOKEN_FILE):
        with open(_DA_TOKEN_FILE, "r") as f:
            return json.load(f).get("access_token", "")
    return ""

def _get_da_refresh():
    if os.path.exists(_DA_TOKEN_FILE):
        with open(_DA_TOKEN_FILE, "r") as f:
            return json.load(f).get("refresh_token", "")
    return ""

DONATIONALERTS_TOKEN = _get_da_token()

def _save_da_tokens(tokens):
    tmp = _DA_TOKEN_FILE + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(tokens, f, indent=2)
    os.replace(tmp, _DA_TOKEN_FILE)

def _parse_jwt_exp(tok):
    try:
        import base64
        parts = (tok or "").split(".")
        if len(parts) < 2:
            return None
        pad = "=" * (-len(parts[1]) % 4)
        payload = json.loads(base64.urlsafe_b64decode(parts[1] + pad))
        exp = payload.get("exp")
        return int(exp) if exp else None
    except Exception:
        return None

def _auth_headers():
    return {"Authorization": f"Bearer {DONATIONALERTS_TOKEN}"}

DATA_FILE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "bot_data.json")

PRICES = {
    "1day":    {"amount": 49,   "label": "1 Day",    "days": 1},
    "7days":   {"amount": 199,  "label": "7 Days",   "days": 7},
    "monthly": {"amount": 499,  "label": "1 Month",  "days": 30},
    "3months": {"amount": 999,  "label": "3 Months", "days": 90},
    "yearly":  {"amount": 1999, "label": "1 Year",   "days": 365},
}

AMOUNT_TO_TYPE = {v["amount"]: k for k, v in PRICES.items()}

LOG_FILE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "telegram_bot.log")
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(message)s",
    handlers=[
        logging.FileHandler(LOG_FILE, encoding="utf-8"),
        logging.StreamHandler(),
    ],
)
log = logging.getLogger(__name__)

DA_POLL_INTERVAL = 15
SETUP_ASSET_NAME = "LichessBotSetup.zip"
PROCESSED_DONATIONS_MAX = 500
PENDING_MATCH_TTL = 24 * 3600
PENDING_PAYMENT_TTL = 24 * 3600
RECENT_PENDING_WINDOW = 30 * 60
DONATIONS_LOG_MAX = 50
AUTO_MATCH_THRESHOLD = 90
SOFT_MATCH_THRESHOLD = 50
RECONNECT_NOTIFY_EVERY = 10
CLEANUP_INTERVAL = 5 * 60
_last_cleanup = 0

DA_PROXY = os.environ.get("LICHESS_DA_PROXY") or None
if DA_PROXY and DA_PROXY.lower().startswith("socks://"):
    DA_PROXY = "socks5://" + DA_PROXY[len("socks://"):]

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import license as L
from keygen import generate_key

def verify_key(key_str):
    try:
        return L.validate(key_str)
    except L.LicenseError:
        return None

SETUP_FILE_PATHS = [
    os.path.join(os.path.dirname(os.path.abspath(__file__)), "dist", "LichessBotSetup.zip"),
]

def _find_local_setup():
    for p in SETUP_FILE_PATHS:
        if os.path.exists(p):
            return p
    return None

_SETUP_INSTRUCTIONS = (
    "📦 <b>How to install:</b>\n"
    "1. Right-click the ZIP → <b>Extract all</b>\n"
    "2. Open the extracted <b>LichessBotSetup</b> folder\n"
    "3. Double-click <b>LichessBotSetup.exe</b>\n"
    "4. Confirm the UAC prompt → follow the installer"
)

def _send_setup(chat_id, version=None):
    _sync_release_fields_from_disk(data)
    fid = data.get("update_file_id")
    ver = version or data.get("update_version", "latest")
    changelog = data.get("update_changelog") or ""
    caption = f"<b>Lichess Bot Setup v{ver}</b>"
    if changelog:
        caption += f"\n\n<b>What's new:</b>\n{changelog}"
    caption += f"\n\n{_SETUP_INSTRUCTIONS}"
    if len(caption) > 1020:
        caption = caption[:1017] + "..."

    if fid:
        try:
            bot.send_document(chat_id, fid, caption=caption)
            return True
        except Exception as e:
            log.warning(f"send via file_id failed ({e}), falling back")
            data["update_file_id"] = None

    local = _find_local_setup()
    if local:
        size_mb = os.path.getsize(local) / (1024 * 1024)
        if size_mb <= 49:
            with open(local, "rb") as f:
                fname = os.path.basename(local)
                result = bot.send_document(chat_id, f, caption=caption,
                                            visible_file_name=fname)
                data["update_file_id"] = result.document.file_id
                data["update_version"] = ver
                _save_data(data)
            return True

    bot.send_message(chat_id,
        f"⚠️ <b>Setup v{ver} not available right now.</b>\n"
        f"Ask admin to re-run <code>python release.py</code>.")
    return False

def _load_data():
    if os.path.exists(DATA_FILE):
        with open(DATA_FILE, "r", encoding="utf-8") as f:
            return json.load(f)
    return {"verified_users": {}, "update_file_id": None, "update_version": None,
            "admin_ids": ADMIN_IDS, "pending_payments": {},
            "users_cache": {}, "processed_donation_ids": [],
            "pending_matches": {}, "donations_log": []}

_RELEASE_FIELDS = (
    "update_file_id", "update_version", "update_changelog",
    "update_message_id", "update_chat_id", "download_url",
)

def _sync_release_fields_from_disk(d):
    try:
        if os.path.exists(DATA_FILE):
            with open(DATA_FILE, "r", encoding="utf-8") as f:
                disk = json.load(f)
            for k in _RELEASE_FIELDS:
                if k in disk and disk[k] != d.get(k):
                    d[k] = disk[k]
    except Exception as e:
        log.warning(f"[DATA] sync release fields failed: {e}")

def _save_data(d):
    _sync_release_fields_from_disk(d)
    with open(DATA_FILE, "w", encoding="utf-8") as f:
        json.dump(d, f, indent=2, ensure_ascii=False)

data = _load_data()
if data.get("admin_ids"):
    ADMIN_IDS.clear()
    ADMIN_IDS.extend(data["admin_ids"])

bot = telebot.TeleBot(BOT_TOKEN, parse_mode="HTML")

def is_verified(cid): return str(cid) in data.get("verified_users", {})
def is_admin(cid): return cid in ADMIN_IDS
def is_banned(cid): return str(cid) in data.get("banned_users", {})

def _notify_admins(text, reply_markup=None):
    for aid in ADMIN_IDS:
        try:
            bot.send_message(aid, text, reply_markup=reply_markup)
        except Exception as e:
            log.error(f"notify_admins failed for {aid}: {e}")

def _notify_admins_bg(text, reply_markup=None):
    threading.Thread(target=_notify_admins, args=(text, reply_markup), daemon=True).start()

def _remember_user(m):
    try:
        user = m.from_user
        if not user:
            return
        users = data.setdefault("users_cache", {})
        users[str(m.chat.id)] = {
            "username": (user.username or "").lower(),
            "name": f"{user.first_name or ''} {user.last_name or ''}".strip(),
            "last_seen": int(time.time()),
        }
        _save_data(data)
    except Exception as e:
        log.error(f"remember_user failed: {e}")

_TRANSLIT = {
    "а":"a","б":"b","в":"v","г":"g","д":"d","е":"e","ё":"e","ж":"zh","з":"z",
    "и":"i","й":"y","к":"k","л":"l","м":"m","н":"n","о":"o","п":"p","р":"r",
    "с":"s","т":"t","у":"u","ф":"f","х":"h","ц":"c","ч":"ch","ш":"sh","щ":"sch",
    "ъ":"","ы":"y","ь":"","э":"e","ю":"yu","я":"ya",
}
def _translit(s):
    return "".join(_TRANSLIT.get(c, c) for c in (s or "").lower())

def _normalize_uname(s):
    s = (s or "").lower().strip().lstrip("@")
    s = re.sub(r"[\s_\-.]+", "", s)
    return s

def _normalize_name(s):
    s = (s or "").lower().strip()
    s = re.sub(r"[\s_\-.]+", "", s)
    return s

def _find_cid_by_username(uname):
    n = _normalize_uname(uname)
    if not n:
        return None
    t = _translit(n)
    for source in ("users_cache", "pending_payments", "verified_users"):
        for cid, ud in data.get(source, {}).items():
            stored = _normalize_uname(ud.get("username"))
            if not stored:
                continue
            if stored == n or _translit(stored) == t:
                try:
                    return int(cid)
                except ValueError:
                    return None
    return None

def _user_label(cid):
    cid_s = str(cid)
    ud = (data.get("verified_users", {}).get(cid_s)
          or data.get("pending_payments", {}).get(cid_s)
          or data.get("users_cache", {}).get(cid_s)
          or {})
    uname = ud.get("username") or ""
    name = ud.get("name") or ""
    parts = []
    if name: parts.append(name)
    if uname: parts.append(f"@{uname}")
    parts.append(f"<code>{cid}</code>")
    return " ".join(parts)

_key_attempts = {}
def _check_rate_limit(cid):
    now = time.time()
    attempts = _key_attempts.get(cid, [])
    attempts = [t for t in attempts if now - t < 600]
    if len(attempts) >= 5:
        return False
    attempts.append(now)
    _key_attempts[cid] = attempts
    return True

def _cleanup_expired(force=False):
    global _last_cleanup
    now = time.time()
    if not force and now - _last_cleanup < CLEANUP_INTERVAL:
        return
    _last_cleanup = now
    pending = data.get("pending_payments", {})
    for cid in list(pending.keys()):
        if now - pending[cid].get("time", 0) > PENDING_PAYMENT_TTL:
            del pending[cid]
    matches = data.get("pending_matches", {})
    for did in list(matches.keys()):
        if now - matches[did].get("created", 0) > PENDING_MATCH_TTL:
            del matches[did]

def _log_donation(donation, status, matched_cid=None, reason=None):
    entry = {
        "id": donation.get("id"),
        "time": int(time.time()),
        "username": donation.get("username"),
        "name": donation.get("name"),
        "amount": donation.get("amount"),
        "currency": donation.get("currency"),
        "message": donation.get("message"),
        "status": status,
        "matched_cid": matched_cid,
        "reason": reason,
    }
    dlog = data.setdefault("donations_log", [])
    dlog.append(entry)
    if len(dlog) > DONATIONS_LOG_MAX:
        data["donations_log"] = dlog[-DONATIONS_LOG_MAX:]

def main_kb(cid):
    kb = types.ReplyKeyboardMarkup(resize_keyboard=True)
    if is_admin(cid):
        kb.row("📥 Get Update", "🔄 Check Version")
        kb.row("ℹ️ My License", "🛒 Buy License")
        kb.row("⚙️ Admin", "📨 Support Inbox")
    elif is_verified(cid):
        kb.row("📥 Get Update", "🔄 Check Version")
        kb.row("ℹ️ My License", "🛒 Buy License")
        kb.row("💬 Support")
    else:
        kb.row("🛒 Buy License", "💬 Support")
    return kb

@bot.message_handler(commands=["start"])
def cmd_start(m):
    cid = m.chat.id
    _remember_user(m)
    if is_banned(cid):
        reason = data.get("banned_users", {}).get(str(cid), {}).get("reason", "")
        bot.send_message(cid, f"⛔ Your account is banned.{(' Reason: ' + reason) if reason else ''}")
        return

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
            f"1. Click <b>🛒 Buy License</b> and pick a plan\n"
            f"2. Send donation with <b>exact amount</b>\n"
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
    if not is_admin(cid):
        bot.send_message(cid, "⛔ Access denied.")
        return
    _show_admin_panel(cid)

def _show_admin_panel(cid):
    users = data.get("verified_users", {})
    banned = data.get("banned_users", {})
    pending = data.get("pending_payments", {})
    ver = data.get("update_version", "none")
    has_file = "✅" if data.get("update_file_id") else "❌"

    kb = types.InlineKeyboardMarkup(row_width=2)
    kb.add(
        types.InlineKeyboardButton("👥 Users", callback_data="adm_users"),
        types.InlineKeyboardButton("🚫 Banned", callback_data="adm_banned"),
    )
    kb.add(
        types.InlineKeyboardButton("🔑 Issue Key", callback_data="adm_genkey"),
        types.InlineKeyboardButton("🚀 Release", callback_data="adm_release"),
    )
    kb.add(
        types.InlineKeyboardButton("💸 Donations", callback_data="adm_donations"),
        types.InlineKeyboardButton("⏳ Pending", callback_data="adm_pending"),
    )
    kb.add(
        types.InlineKeyboardButton("🔄 Refresh", callback_data="adm_refresh"),
    )

    bot.send_message(cid,
        f"📊 <b>Admin Panel</b>\n\n"
        f"👥 Users: <b>{len(users)}</b>\n"
        f"🚫 Banned: <b>{len(banned)}</b>\n"
        f"⏳ Pending payments: <b>{len(pending)}</b>\n"
        f"📦 Version: <b>v{ver}</b>\n"
        f"📁 File: {has_file}",
        reply_markup=kb)

@bot.callback_query_handler(func=lambda c: c.data and c.data.startswith("adm_"))
def cb_admin(c):
    cid = c.message.chat.id
    if not is_admin(cid):
        bot.answer_callback_query(c.id, "⛔ Access denied.")
        return

    action = c.data

    if action == "adm_refresh":
        bot.answer_callback_query(c.id, "Refreshed")
        bot.delete_message(cid, c.message.message_id)
        _show_admin_panel(cid)

    elif action == "adm_users":
        bot.answer_callback_query(c.id)
        users = data.get("verified_users", {})
        if not users:
            bot.send_message(cid, "No verified users.")
            return
        lines = ["👥 <b>Verified Users</b>\n"]
        for uid, ud in list(users.items())[:30]:
            name = ud.get("name") or "?"
            uname = f"@{ud['username']}" if ud.get("username") else ""
            ltype = ud.get("info", {}).get("type", "?")
            lines.append(f"• <code>{uid}</code> {name} {uname} — {ltype}")
        bot.send_message(cid, "\n".join(lines))

    elif action == "adm_banned":
        bot.answer_callback_query(c.id)
        banned = data.get("banned_users", {})
        if not banned:
            bot.send_message(cid, "No banned users.\n\nTo ban: <code>/ban USER_ID reason</code>")
            return
        lines = ["🚫 <b>Banned Users</b>\n"]
        for uid, bd in banned.items():
            reason = bd.get("reason", "")
            lines.append(f"• <code>{uid}</code> — {reason or 'no reason'}")
        lines.append(f"\nTo unban: <code>/unban USER_ID</code>")
        bot.send_message(cid, "\n".join(lines))

    elif action == "adm_genkey":
        bot.answer_callback_query(c.id)
        kb = types.InlineKeyboardMarkup(row_width=2)
        for key_type, info in PRICES.items():
            kb.add(types.InlineKeyboardButton(
                f"{info['label']} ({info['amount']}₽)",
                callback_data=f"genkey_{key_type}"))
        bot.send_message(cid, "🔑 <b>Generate Key</b>\n\nSelect plan:", reply_markup=kb)

    elif action == "adm_release":
        bot.answer_callback_query(c.id, "Starting release...")
        bot.send_message(cid, "🚀 Starting release pipeline...")
        threading.Thread(target=_run_release, args=(cid,), daemon=True).start()

    elif action == "adm_donations":
        bot.answer_callback_query(c.id)
        dlog = data.get("donations_log", [])
        if not dlog:
            bot.send_message(cid, "💸 No donations logged yet.")
            return
        lines = ["💸 <b>Recent donations</b>\n"]
        for e in dlog[-15:]:
            ts = time.strftime("%m-%d %H:%M", time.localtime(e.get("time", 0)))
            icon = {"auto_sale": "✅", "soft_match": "💡", "ambiguous": "❓",
                    "unknown_amount": "⚠️", "not_identified": "⚠️",
                    "ignored": "🚫", "manual": "🖐", "error": "❌"}.get(e.get("status"), "•")
            lines.append(
                f"{icon} {ts} — {e.get('username') or '?'} "
                f"{e.get('amount')}{e.get('currency') or ''} "
                f"[{e.get('status')}]"
                + (f" → <code>{e['matched_cid']}</code>" if e.get("matched_cid") else ""))
        bot.send_message(cid, "\n".join(lines))

    elif action == "adm_pending":
        bot.answer_callback_query(c.id)
        pending = data.get("pending_payments", {})
        if not pending:
            bot.send_message(cid, "⏳ No pending payments.")
            return
        now = time.time()
        lines = ["⏳ <b>Pending payments</b>\n"]
        for pid, pd in sorted(pending.items(), key=lambda x: -x[1].get("time", 0))[:20]:
            age = int((now - pd.get("time", 0)) / 60)
            plan = pd.get("plan") or "?"
            uname = f"@{pd['username']}" if pd.get("username") else ""
            lines.append(f"• <code>{pid}</code> {uname} — plan:{plan} ({age}m ago)")
        bot.send_message(cid, "\n".join(lines))

@bot.callback_query_handler(func=lambda c: c.data and c.data.startswith("genkey_"))
def cb_genkey(c):
    cid = c.message.chat.id
    if not is_admin(cid):
        bot.answer_callback_query(c.id, "⛔")
        return
    key_type = c.data.replace("genkey_", "")
    if key_type not in PRICES:
        bot.answer_callback_query(c.id, "Invalid type")
        return
    bot.answer_callback_query(c.id)
    try:
        key = generate_key(key_type)
        info = L.validate(key)
        bot.send_message(cid,
            f"🔑 <b>Key Generated</b>\n\n"
            f"<code>{key}</code>\n\n"
            f"Type: {info['type']}\nExpires: {info['expiry']}\nDays: {info['days_left']}")
    except Exception as e:
        bot.send_message(cid, f"❌ Error: {e}")

@bot.message_handler(commands=["reload"])
def cmd_reload(m):
    if not is_admin(m.chat.id):
        return
    _sync_release_fields_from_disk(data)
    ver = data.get("update_version", "?")
    fid = (data.get("update_file_id") or "")[:30]
    bot.send_message(m.chat.id,
        f"🔄 Data reloaded.\n"
        f"Version: <b>v{ver}</b>\n"
        f"file_id: <code>{fid}...</code>")

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
    _remember_user(m)
    lines = ["💳 <b>Select a plan:</b>\n"]
    for key_type, info in PRICES.items():
        lines.append(f"• <b>{info['label']}</b> — {info['amount']} ₽")
    lines.append(
        f"\nTap a plan below. We'll remember your choice so the key arrives automatically, "
        f"even if your DonationAlerts message is empty.\n\n"
        f"⚠️ Send the <b>exact amount</b> for the plan you picked.")

    kb = types.InlineKeyboardMarkup(row_width=1)
    for key_type, info in PRICES.items():
        kb.add(types.InlineKeyboardButton(
            f"💳 {info['label']} — {info['amount']} ₽",
            callback_data=f"buy_{key_type}"))

    bot.send_message(cid, "\n".join(lines), reply_markup=kb)

@bot.callback_query_handler(func=lambda c: c.data and c.data.startswith("buy_"))
def cb_buy_plan(c):
    cid = c.message.chat.id
    key_type = c.data.replace("buy_", "", 1)
    if key_type not in PRICES:
        bot.answer_callback_query(c.id, "Invalid plan")
        return
    info = PRICES[key_type]

    _cleanup_expired()
    user = c.from_user
    data.setdefault("pending_payments", {})[str(cid)] = {
        "time": time.time(),
        "username": (user.username or "").lower() if user else None,
        "name": f"{user.first_name or ''} {user.last_name or ''}".strip() if user else None,
        "plan": key_type,
        "amount": info["amount"],
    }
    _save_data(data)

    bot.answer_callback_query(c.id, f"Plan saved: {info['label']}")

    kb = types.InlineKeyboardMarkup()
    kb.add(types.InlineKeyboardButton(
        f"💳 Pay {info['amount']} ₽ via DonationAlerts", url=DONATE_URL))

    bot.send_message(cid,
        f"✅ <b>Plan selected: {info['label']} — {info['amount']} ₽</b>\n\n"
        f"1. Click the button below\n"
        f"2. Donate <b>exactly {info['amount']} ₽</b>\n"
        f"3. Message field: <code>{cid}</code> (recommended)\n"
        f"   <i>If you forget — we'll try to match you by username automatically.</i>\n\n"
        f"The key will be sent here as soon as the donation arrives.",
        reply_markup=kb)

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

@bot.message_handler(func=lambda m: m.text == "⚙️ Admin")
def btn_admin(m):
    if is_admin(m.chat.id):
        _show_admin_panel(m.chat.id)

@bot.message_handler(func=lambda m: m.text == "📨 Support Inbox")
def btn_support_inbox(m):
    if not is_admin(m.chat.id):
        return
    tickets = data.get("support_tickets", [])
    if not tickets:
        bot.send_message(m.chat.id, "📨 Inbox is empty.")
        return
    for t in tickets[-10:]:
        uid = t["user_id"]
        name = t.get("name", "?")
        uname = t.get("username", "")
        text = t.get("text", "")
        ts = t.get("time", "")
        kb = types.InlineKeyboardMarkup()
        kb.add(
            types.InlineKeyboardButton("💬 Reply", callback_data=f"reply_{uid}"),
            types.InlineKeyboardButton("🚫 Ban", callback_data=f"ban_{uid}"),
        )
        bot.send_message(m.chat.id,
            f"💬 <b>From:</b> {name} {('@' + uname) if uname else ''}\n"
            f"<b>ID:</b> <code>{uid}</code>\n"
            f"<b>Time:</b> {ts}\n\n{text}",
            reply_markup=kb)

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
    uname = user.username or ""
    ts = time.strftime("%Y-%m-%d %H:%M")

    ticket = {"user_id": m.chat.id, "name": name, "username": uname,
              "text": m.text, "time": ts}
    data.setdefault("support_tickets", []).append(ticket)
    if len(data["support_tickets"]) > 50:
        data["support_tickets"] = data["support_tickets"][-50:]
    _save_data(data)

    kb = types.InlineKeyboardMarkup()
    kb.add(
        types.InlineKeyboardButton("💬 Reply", callback_data=f"reply_{m.chat.id}"),
        types.InlineKeyboardButton("🚫 Ban", callback_data=f"ban_{m.chat.id}"),
    )
    for aid in ADMIN_IDS:
        try:
            bot.send_message(aid,
                f"💬 <b>Support</b>\nFrom: {name} ({('@' + uname) if uname else 'no username'}, ID: <code>{m.chat.id}</code>)\n"
                f"Verified: {'✅' if is_verified(m.chat.id) else '❌'}\n\n{m.text}",
                reply_markup=kb)
        except Exception: pass
    bot.send_message(m.chat.id, "✅ Sent to support!", reply_markup=main_kb(m.chat.id))

@bot.message_handler(func=lambda m: m.text == "🔄 Check Version")
def btn_check_version(m):
    cid = m.chat.id
    if not is_verified(cid):
        bot.send_message(cid, "⛔ Verify your key first.", reply_markup=main_kb(cid))
        return

    _sync_release_fields_from_disk(data)
    ver = data.get("update_version")
    fid = data.get("update_file_id")
    changelog = data.get("update_changelog") or ""

    if not ver:
        bot.send_message(cid, "No version info available yet.", reply_markup=main_kb(cid))
        return

    kb = types.InlineKeyboardMarkup()
    if fid:
        kb.add(types.InlineKeyboardButton("📥 Download", callback_data="dl_update"))

    text = f"🔄 <b>Latest version: v{ver}</b>"
    if changelog:
        text += f"\n\n<b>What's new:</b>\n{changelog}"
    text += ("\n\nCheck the version on your PC (shown in the app title bar). "
             "If it doesn't match - download the update below.")
    bot.send_message(cid, text, reply_markup=kb)

@bot.callback_query_handler(func=lambda c: c.data and c.data.startswith("reply_"))
def cb_reply(c):
    cid = c.message.chat.id
    if not is_admin(cid):
        bot.answer_callback_query(c.id, "⛔")
        return
    target = c.data.replace("reply_", "")
    bot.answer_callback_query(c.id)
    bot.send_message(cid, f"💬 Type your reply to <code>{target}</code>:")
    bot.register_next_step_handler(c.message, _send_reply, int(target))

def _send_reply(m, target_id):
    if not m.text or m.text.startswith("/"):
        bot.send_message(m.chat.id, "Cancelled.")
        return
    try:
        bot.send_message(target_id,
            f"💬 <b>Support Reply</b>\n\n{m.text}",
            reply_markup=main_kb(target_id))
        bot.send_message(m.chat.id, f"✅ Reply sent to <code>{target_id}</code>")
    except Exception as e:
        bot.send_message(m.chat.id, f"❌ Failed: {e}")

@bot.callback_query_handler(func=lambda c: c.data and c.data.startswith("ban_"))
def cb_ban_quick(c):
    cid = c.message.chat.id
    if not is_admin(cid):
        bot.answer_callback_query(c.id, "⛔")
        return
    target = c.data.replace("ban_", "")
    bot.answer_callback_query(c.id)
    data.setdefault("banned_users", {})[target] = {"reason": "Support abuse", "time": time.time()}
    _save_data(data)
    bot.send_message(cid, f"🚫 User <code>{target}</code> banned.")

@bot.message_handler(commands=["ban"])
def cmd_ban(m):
    if not is_admin(m.chat.id):
        return
    parts = m.text.split(maxsplit=2)
    if len(parts) < 2:
        bot.send_message(m.chat.id, "Usage: <code>/ban USER_ID reason</code>")
        return
    uid = parts[1].strip()
    reason = parts[2].strip() if len(parts) > 2 else ""
    data.setdefault("banned_users", {})[uid] = {"reason": reason, "time": time.time()}
    _save_data(data)
    bot.send_message(m.chat.id, f"🚫 User <code>{uid}</code> banned.{(' Reason: ' + reason) if reason else ''}")

@bot.message_handler(commands=["unban"])
def cmd_unban(m):
    if not is_admin(m.chat.id):
        return
    parts = m.text.split(maxsplit=1)
    if len(parts) < 2:
        bot.send_message(m.chat.id, "Usage: <code>/unban USER_ID</code>")
        return
    uid = parts[1].strip()
    if uid in data.get("banned_users", {}):
        del data["banned_users"][uid]
        _save_data(data)
        bot.send_message(m.chat.id, f"✅ User <code>{uid}</code> unbanned.")
    else:
        bot.send_message(m.chat.id, f"User <code>{uid}</code> is not banned.")

def _give_key(target, plan, source="manual"):
    key = generate_key(plan)
    info = L.validate(key)
    bot.send_message(target,
        f"🎉 <b>Payment received!</b>\n\n"
        f"Your key:\n<code>{key}</code>\n\n"
        f"Type: {info['type']}\nExpires: {info['expiry']}\nDays: {info['days_left']}\n\n"
        f"Paste this key in the bot app to activate.",
        reply_markup=main_kb(target))
    existing = data.setdefault("verified_users", {}).get(str(target), {})
    data["verified_users"][str(target)] = {
        "key": key, "info": info,
        "username": existing.get("username"),
        "name": existing.get("name")}
    data.get("pending_payments", {}).pop(str(target), None)
    _save_data(data)
    log.info(f"[KEY GIVEN] {target} → {plan} (source={source})")
    return key, info

@bot.message_handler(commands=["give"])
def cmd_give(m):
    if not is_admin(m.chat.id):
        return
    parts = m.text.split()
    if len(parts) < 3:
        bot.send_message(m.chat.id,
            "Usage: <code>/give CHAT_ID PLAN</code>\n"
            f"Plans: {', '.join(PRICES.keys())}")
        return
    try:
        target = int(parts[1].strip())
    except ValueError:
        bot.send_message(m.chat.id, "❌ CHAT_ID must be integer.")
        return
    plan = parts[2].strip()
    if plan not in PRICES:
        bot.send_message(m.chat.id, f"❌ Invalid plan. Use: {', '.join(PRICES.keys())}")
        return
    try:
        _give_key(target, plan, source="cmd_give")
        bot.send_message(m.chat.id,
            f"✅ Key sent to <code>{target}</code>\nPlan: {PRICES[plan]['label']}")
    except Exception as e:
        bot.send_message(m.chat.id, f"❌ Error: {e}")
        log.exception("give failed")

@bot.message_handler(commands=["reply"])
def cmd_reply(m):
    if not is_admin(m.chat.id):
        return
    parts = m.text.split(maxsplit=2)
    if len(parts) < 3:
        bot.send_message(m.chat.id, "Usage: <code>/reply USER_ID message</code>")
        return
    try:
        target = int(parts[1].strip())
        text = parts[2].strip()
        bot.send_message(target, f"💬 <b>Support Reply</b>\n\n{text}", reply_markup=main_kb(target))
        bot.send_message(m.chat.id, f"✅ Sent to <code>{target}</code>")
    except Exception as e:
        bot.send_message(m.chat.id, f"❌ Failed: {e}")

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
    _remember_user(m)
    if is_banned(cid):
        bot.send_message(cid, "⛔ Your account is banned.")
        return
    text = (m.text or "").strip()
    if not text: return

    cleaned = text.upper().replace("-", "").replace(" ", "")
    if len(cleaned) < 20 or len(cleaned) > 50:
        bot.send_message(cid, "❓ Send a <b>license key</b> or use buttons.", reply_markup=main_kb(cid))
        return

    if not _check_rate_limit(cid):
        bot.send_message(cid, "⏳ Too many attempts. Wait 10 minutes.", reply_markup=main_kb(cid))
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

    _notify_admins(
        f"🆕 Verified: {m.from_user.first_name} (@{m.from_user.username or 'none'}, {cid})\n"
        f"License: {info['type']} — {info['expiry']}")

def _identify_candidates(donation):
    msg = (donation.get("message") or "").strip()
    da_username = donation.get("username") or ""
    da_name = donation.get("name") or ""
    amount = int(round(float(donation.get("amount", 0))))
    now = time.time()

    found = []

    if msg:
        m = re.search(r"(?:id[:\s]*)?(\d{6,12})", msg, re.I)
        if m:
            try:
                found.append((int(m.group(1)), "message_id", 100))
            except ValueError:
                pass
        m = re.search(r"@(\w{3,32})", msg)
        if m:
            cid = _find_cid_by_username(m.group(1))
            if cid:
                found.append((cid, "message_@username", 95))
        for word in re.findall(r"[A-Za-zА-Яа-яЁё0-9_]{3,32}", msg):
            if word.isdigit():
                continue
            cid = _find_cid_by_username(word)
            if cid:
                found.append((cid, "message_username_word", 80))

    if da_username:
        n_da = _normalize_uname(da_username)
        t_da = _translit(n_da)
        if n_da:
            for source in ("pending_payments", "users_cache", "verified_users"):
                for cid, ud in data.get(source, {}).items():
                    stored = _normalize_uname(ud.get("username"))
                    if not stored:
                        continue
                    if stored == n_da:
                        found.append((int(cid), f"da_username:{source}", 85))
                    elif _translit(stored) == t_da:
                        found.append((int(cid), f"da_username_translit:{source}", 70))

    if da_name:
        n_dn = _normalize_name(da_name)
        t_dn = _translit(n_dn)
        if n_dn and len(n_dn) >= 3:
            for cid, ud in data.get("users_cache", {}).items():
                stored = _normalize_name(ud.get("name"))
                if not stored or len(stored) < 3:
                    continue
                if stored == n_dn:
                    found.append((int(cid), "da_name", 60))
                elif _translit(stored) == t_dn:
                    found.append((int(cid), "da_name_translit", 50))

    matched_plans = {kt for pa, kt in AMOUNT_TO_TYPE.items() if abs(amount - pa) <= 5}
    for cid_s, pd in data.get("pending_payments", {}).items():
        ts = pd.get("time", 0)
        if now - ts > RECENT_PENDING_WINDOW:
            continue
        try:
            cid = int(cid_s)
        except ValueError:
            continue
        plan = pd.get("plan")
        if plan and plan in matched_plans:
            found.append((cid, "pending_plan_amount_match", 75))
        elif not plan:
            found.append((cid, "pending_recent", 40))

    best = {}
    for cid, reason, conf in found:
        if cid not in best or best[cid][1] < conf:
            best[cid] = (reason, conf)
    ranked = sorted(
        ((cid, r, c) for cid, (r, c) in best.items()),
        key=lambda x: -x[2]
    )
    return ranked

def _resolve_key_type(amount):
    key_type = AMOUNT_TO_TYPE.get(amount)
    if key_type:
        return key_type, False
    for pa, kt in AMOUNT_TO_TYPE.items():
        if abs(amount - pa) <= 5:
            return kt, True
    return None, False

def _store_pending_match(donation, key_type, candidates):
    did = str(donation.get("id") or int(time.time() * 1000))
    data.setdefault("pending_matches", {})[did] = {
        "donation": {
            "id": donation.get("id"),
            "username": donation.get("username"),
            "name": donation.get("name"),
            "amount": donation.get("amount"),
            "currency": donation.get("currency"),
            "message": donation.get("message"),
        },
        "key_type": key_type,
        "candidates": [[cid, r, c] for cid, r, c in candidates[:5]],
        "created": time.time(),
        "resolved": False,
    }
    _save_data(data)
    return did

def _process_donation(d):
    _cleanup_expired()

    did = d.get("id")
    if did is not None:
        processed = data.setdefault("processed_donation_ids", [])
        if did in processed:
            log.info(f"[DEDUP] Donation {did} already processed, skipping")
            return
        processed.append(did)
        if len(processed) > PROCESSED_DONATIONS_MAX:
            data["processed_donation_ids"] = processed[-PROCESSED_DONATIONS_MAX:]

    username = d.get("username") or d.get("name") or "Anonymous"
    amount_f = float(d.get("amount", 0))
    amount = int(round(amount_f))
    currency = d.get("currency", "RUB")
    msg = d.get("message", "") or ""

    log.info(f"[DONATE] id={did} {username}: {amount} {currency} — {msg}")

    key_type, fuzzy_amount = _resolve_key_type(amount)

    if not key_type:
        _log_donation(d, "unknown_amount")
        did_key = _store_pending_match(d, None, [])
        kb = types.InlineKeyboardMarkup(row_width=2)
        for kt, info in PRICES.items():
            kb.add(types.InlineKeyboardButton(
                f"{info['label']} ({info['amount']}₽)",
                callback_data=f"pm_pickplan:{did_key}:{kt}"))
        kb.add(types.InlineKeyboardButton("🚫 Ignore", callback_data=f"pm_ignore:{did_key}"))
        _notify_admins(
            f"⚠️ <b>Payment received — unknown amount</b>\n\n"
            f"From: {username}\nAmount: <b>{amount} {currency}</b>\n"
            f"Message: <code>{msg or '(empty)'}</code>\n\n"
            f"Pick a plan to issue manually:",
            reply_markup=kb)
        return

    candidates = _identify_candidates(d)
    log.info(f"[MATCH] candidates for donation {did}: {candidates}")

    if not candidates:
        _log_donation(d, "not_identified")
        _notify_admins(
            f"⚠️ <b>Payment received — recipient unknown</b>\n\n"
            f"From DA: {username}\nAmount: <b>{amount} {currency}</b> → {PRICES[key_type]['label']}\n"
            f"Message: <code>{msg or '(empty)'}</code>\n\n"
            f"No user match. Issue manually: <code>/give CHAT_ID {key_type}</code>")
        return

    top_cid, top_reason, top_conf = candidates[0]
    try:
        _give_key(top_cid, key_type, source=f"auto:{top_reason}")
        _log_donation(d, "auto_sale", matched_cid=top_cid, reason=top_reason)
        _notify_admins_bg(
            f"💰 <b>Payment confirmed — key issued</b>\n"
            f"User: {_user_label(top_cid)}\n"
            f"Plan: <b>{PRICES[key_type]['label']}</b> ({amount} {currency})"
            f"{' <i>(fuzzy)</i>' if fuzzy_amount else ''}\n"
            f"From DA: {username}\n"
            f"Match: <code>{top_reason}</code> ({top_conf}%)")
    except Exception as e:
        _log_donation(d, "error", matched_cid=top_cid, reason=str(e))
        _notify_admins(
            f"❌ <b>Key generation failed!</b>\n"
            f"User: {_user_label(top_cid)}\n"
            f"Plan: {PRICES[key_type]['label']}\nError: <code>{e}</code>\n\n"
            f"Issue manually: <code>/give {top_cid} {key_type}</code>")
        log.exception("auto give failed")

def _label_short(cid):
    cid_s = str(cid)
    ud = (data.get("verified_users", {}).get(cid_s)
          or data.get("pending_payments", {}).get(cid_s)
          or data.get("users_cache", {}).get(cid_s)
          or {})
    uname = ud.get("username") or ""
    name = ud.get("name") or ""
    if uname:
        return f"@{uname}"
    if name:
        return name
    return str(cid)

@bot.callback_query_handler(func=lambda c: c.data and c.data.startswith("pm_"))
def cb_payment_match(c):
    admin_cid = c.message.chat.id
    if not is_admin(admin_cid):
        bot.answer_callback_query(c.id, "⛔")
        return

    parts = c.data.split(":")
    action = parts[0]

    if action == "pm_match" and len(parts) >= 3:
        did_key, target_s = parts[1], parts[2]
        pm = data.get("pending_matches", {}).get(did_key)
        if not pm or pm.get("resolved"):
            bot.answer_callback_query(c.id, "Already resolved")
            return
        key_type = pm.get("key_type")
        if not key_type:
            bot.answer_callback_query(c.id, "Plan missing, pick one first")
            return
        try:
            target = int(target_s)
            _give_key(target, key_type, source="admin_pick")
            pm["resolved"] = True
            pm["resolved_by"] = admin_cid
            pm["resolved_to"] = target
            _save_data(data)
            for e in data.get("donations_log", []):
                if e.get("id") == pm.get("donation", {}).get("id"):
                    e["status"] = "manual"
                    e["matched_cid"] = target
                    e["reason"] = "admin_pick"
            _save_data(data)
            bot.answer_callback_query(c.id, "Key sent ✓")
            bot.edit_message_text(
                c.message.text + f"\n\n✅ Resolved → {_user_label(target)} ({key_type})",
                chat_id=admin_cid, message_id=c.message.message_id, parse_mode="HTML")
        except Exception as e:
            bot.answer_callback_query(c.id, f"Error: {e}")

    elif action == "pm_pickplan" and len(parts) >= 3:
        did_key, plan = parts[1], parts[2]
        pm = data.get("pending_matches", {}).get(did_key)
        if not pm or pm.get("resolved"):
            bot.answer_callback_query(c.id, "Already resolved")
            return
        if plan not in PRICES:
            bot.answer_callback_query(c.id, "Invalid plan")
            return
        pm["key_type"] = plan
        candidates = _identify_candidates(pm["donation"])
        pm["candidates"] = [[cid, r, cf] for cid, r, cf in candidates[:5]]
        _save_data(data)
        bot.answer_callback_query(c.id, f"Plan: {plan}")
        kb = types.InlineKeyboardMarkup(row_width=1)
        for cid, r, cf in candidates[:5]:
            kb.add(types.InlineKeyboardButton(
                f"✅ {_label_short(cid)} — {cf}%",
                callback_data=f"pm_match:{did_key}:{cid}"))
        kb.add(types.InlineKeyboardButton("👥 Pick from pending",
                                          callback_data=f"pm_pickuser:{did_key}"))
        kb.add(types.InlineKeyboardButton("🚫 Ignore", callback_data=f"pm_ignore:{did_key}"))
        bot.send_message(admin_cid,
            f"Plan locked: <b>{PRICES[plan]['label']}</b>. Choose recipient:",
            reply_markup=kb)

    elif action == "pm_pickuser" and len(parts) >= 2:
        did_key = parts[1]
        pm = data.get("pending_matches", {}).get(did_key)
        if not pm or pm.get("resolved"):
            bot.answer_callback_query(c.id, "Already resolved")
            return
        bot.answer_callback_query(c.id)
        kb = types.InlineKeyboardMarkup(row_width=1)
        now = time.time()
        added = 0
        for cid_s, pd in sorted(data.get("pending_payments", {}).items(),
                                 key=lambda x: -x[1].get("time", 0)):
            if now - pd.get("time", 0) > PENDING_PAYMENT_TTL:
                continue
            try:
                cid = int(cid_s)
            except ValueError:
                continue
            kb.add(types.InlineKeyboardButton(
                f"{_label_short(cid)}",
                callback_data=f"pm_match:{did_key}:{cid}"))
            added += 1
            if added >= 10:
                break
        kb.add(types.InlineKeyboardButton("🚫 Ignore", callback_data=f"pm_ignore:{did_key}"))
        bot.send_message(admin_cid,
            f"Pending payments ({added}):\n"
            f"If user not here, use <code>/give CHAT_ID {pm.get('key_type') or 'PLAN'}</code>",
            reply_markup=kb)

    elif action == "pm_ignore" and len(parts) >= 2:
        did_key = parts[1]
        pm = data.get("pending_matches", {}).get(did_key)
        if not pm:
            bot.answer_callback_query(c.id, "Unknown")
            return
        pm["resolved"] = True
        pm["resolved_by"] = admin_cid
        pm["ignored"] = True
        _save_data(data)
        for e in data.get("donations_log", []):
            if e.get("id") == pm.get("donation", {}).get("id"):
                e["status"] = "ignored"
        _save_data(data)
        bot.answer_callback_query(c.id, "Ignored")
        bot.edit_message_text(
            c.message.text + "\n\n🚫 Ignored.",
            chat_id=admin_cid, message_id=c.message.message_id, parse_mode="HTML")
    else:
        bot.answer_callback_query(c.id, "Unknown action")

_reconnect_fails = {"ws": 0, "rest": 0}

def _http_client(**kw):
    import httpx
    opts = {"trust_env": False}
    if DA_PROXY:
        opts["proxy"] = DA_PROXY
    opts.update(kw)
    return httpx.AsyncClient(**opts)

_refresh_lock = asyncio.Lock()
_refresh_state = {"last_attempt": 0, "cooldown_until": 0, "last_error_notified": 0}

def _token_scopes(tok):
    try:
        import base64
        parts = (tok or "").split(".")
        if len(parts) < 2:
            return []
        pad = "=" * (-len(parts[1]) % 4)
        payload = json.loads(base64.urlsafe_b64decode(parts[1] + pad))
        return payload.get("scopes", []) or []
    except Exception:
        return []

async def _refresh_da_token(force=False):
    global DONATIONALERTS_TOKEN
    now = time.time()
    if not force and now < _refresh_state["cooldown_until"]:
        return False

    async with _refresh_lock:
        refresh = _get_da_refresh()
        if not refresh:
            log.error("[TOKEN] No refresh_token in da_tokens.json")
            if now - _refresh_state["last_error_notified"] > 3600:
                _notify_admins(
                    "🔴 <b>DA refresh impossible</b>\n"
                    "No refresh_token. Run <code>python da_auth.py</code>.")
                _refresh_state["last_error_notified"] = now
            _refresh_state["cooldown_until"] = now + 3600
            return False

        _refresh_state["last_attempt"] = now
        try:
            async with _http_client(timeout=30.0) as c:
                r = await c.post(
                    "https://www.donationalerts.com/oauth/token",
                    data={
                        "grant_type": "refresh_token",
                        "client_id": _DA_CLIENT_ID,
                        "client_secret": _DA_CLIENT_SECRET,
                        "refresh_token": refresh,
                    })
            if r.status_code != 200:
                log.error(f"[TOKEN] Refresh failed: {r.status_code} {r.text[:300]}")
                _refresh_state["cooldown_until"] = now + 600
                if now - _refresh_state["last_error_notified"] > 3600:
                    _notify_admins(
                        f"🔴 <b>DA token refresh failed ({r.status_code})</b>\n"
                        f"<code>{r.text[:200]}</code>\n\n"
                        f"Run <code>python da_auth.py</code> manually.")
                    _refresh_state["last_error_notified"] = now
                return False
            new_tokens = r.json()
        except Exception as e:
            log.exception("[TOKEN] refresh error")
            _refresh_state["cooldown_until"] = now + 600
            if now - _refresh_state["last_error_notified"] > 3600:
                _notify_admins(f"❌ <b>DA token refresh error</b>\n<code>{e}</code>")
                _refresh_state["last_error_notified"] = now
            return False

        _save_da_tokens(new_tokens)
        DONATIONALERTS_TOKEN = new_tokens.get("access_token", "")
        _refresh_state["cooldown_until"] = 0
        _refresh_state["last_error_notified"] = 0
        exp = _parse_jwt_exp(DONATIONALERTS_TOKEN)
        scopes = _token_scopes(DONATIONALERTS_TOKEN)
        log.info(f"[TOKEN] Refreshed successfully. exp={exp} scopes={scopes}")
        _notify_admins(
            f"🔄 <b>DA token auto-refreshed</b>\n"
            f"Valid until: <code>"
            f"{time.strftime('%Y-%m-%d %H:%M', time.localtime(exp)) if exp else 'unknown'}</code>\n"
            f"Scopes: <code>{' '.join(scopes) or '?'}</code>")
        return True

async def _token_keeper():
    while True:
        try:
            exp = _parse_jwt_exp(DONATIONALERTS_TOKEN)
            now = time.time()
            if exp and (exp - now) < 2 * 24 * 3600:
                log.info(f"[TOKEN] < 48h to expiry ({exp - now:.0f}s), refreshing proactively")
                await _refresh_da_token()
        except Exception as e:
            log.error(f"[TOKEN] keeper error: {e}")
        await asyncio.sleep(6 * 3600)


async def listen_donations():
    import websockets

    api = "https://www.donationalerts.com/api/v1"
    ws_url = "wss://centrifugo.donationalerts.com/connection/websocket"

    while True:
        try:
            async with _http_client() as client:
                r = await client.get(f"{api}/user/oauth", headers=_auth_headers())
                if r.status_code in (401, 403):
                    log.warning(f"[WS] oauth {r.status_code}, attempting token refresh...")
                    if await _refresh_da_token():
                        continue
                    _notify_admins(
                        f"🔴 <b>DonationAlerts auth failed ({r.status_code})</b>\n\n"
                        f"Refresh also failed. Run <code>python da_auth.py</code>.")
                    await asyncio.sleep(300)
                    continue
                r.raise_for_status()
                ud = r.json()["data"]

            user_id = ud["id"]
            socket_token = ud["socket_connection_token"]
            log.info(f"DonationAlerts connected (user_id: {user_id})")
            _reconnect_fails["ws"] = 0

            async with websockets.connect(ws_url) as ws:
                await ws.send(json.dumps({"params": {"token": socket_token}, "id": 1}))
                auth = json.loads(await ws.recv())
                client_id = auth["result"]["client"]

                channel = f"$alerts:donation_{user_id}"
                async with _http_client() as client:
                    r = await client.post(f"{api}/centrifuge/subscribe", headers=_auth_headers(),
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
            _reconnect_fails["ws"] += 1
            log.error(f"DonationAlerts error: {e}, reconnecting in 10s...")
            if _reconnect_fails["ws"] == 1 or _reconnect_fails["ws"] % RECONNECT_NOTIFY_EVERY == 0:
                _notify_admins(
                    f"⚠️ <b>DA WebSocket unstable</b>\n"
                    f"Fails in a row: {_reconnect_fails['ws']}\n"
                    f"Error: <code>{e}</code>")
            await asyncio.sleep(10)

async def poll_donations_rest():
    api = "https://www.donationalerts.com/api/v1"
    first_run = True
    notified_scope_issue = False
    while True:
        if "oauth-donation-index" not in _token_scopes(DONATIONALERTS_TOKEN):
            if not notified_scope_issue:
                _notify_admins(
                    "🟡 <b>REST poller idle</b>\n"
                    "Current token is missing <code>oauth-donation-index</code> scope.\n"
                    "Only WebSocket is active — catch-up of missed donations won't work.\n\n"
                    "Run <code>python da_auth.py</code> once to fix permanently.")
                notified_scope_issue = True
            await asyncio.sleep(30 * 60)
            continue

        try:
            async with _http_client(timeout=30.0) as client:
                r = await client.get(f"{api}/alerts/donations", headers=_auth_headers(),
                                     params={"page": 1})
                if r.status_code in (401, 403):
                    log.warning(f"[REST] {r.status_code}, attempting token refresh...")
                    if await _refresh_da_token():
                        await asyncio.sleep(2)
                        continue
                    _reconnect_fails["rest"] += 1
                    if _reconnect_fails["rest"] == 1 or _reconnect_fails["rest"] % RECONNECT_NOTIFY_EVERY == 0:
                        _notify_admins(
                            f"🔴 <b>DA REST auth failed ({r.status_code})</b> and refresh also failed.\n"
                            f"Run <code>python da_auth.py</code>.")
                    await asyncio.sleep(DA_POLL_INTERVAL)
                    continue
                r.raise_for_status()
                _reconnect_fails["rest"] = 0
                donations = r.json().get("data", []) or []

            processed = set(data.get("processed_donation_ids", []))
            if first_run and not processed:
                ids = [d.get("id") for d in donations if d.get("id") is not None]
                data["processed_donation_ids"] = ids[-PROCESSED_DONATIONS_MAX:]
                _save_data(data)
                log.info(f"[REST SYNC] Seeded {len(ids)} existing donations (no retroactive processing)")
            else:
                new_items = []
                for d in donations:
                    did = d.get("id")
                    if did is not None and did not in processed:
                        new_items.append(d)
                for d in reversed(new_items):
                    log.warning(f"[REST SYNC] Catching up missed donation {d.get('id')}: "
                                f"{d.get('username')} {d.get('amount')} {d.get('currency')}")
                    _process_donation(d)
            first_run = False
        except Exception as e:
            _reconnect_fails["rest"] += 1
            log.error(f"REST sync error: {e}")
            if _reconnect_fails["rest"] == 1 or _reconnect_fails["rest"] % RECONNECT_NOTIFY_EVERY == 0:
                _notify_admins(
                    f"⚠️ <b>DA REST poller error</b>\n"
                    f"Fails in a row: {_reconnect_fails['rest']}\n"
                    f"Error: <code>{e}</code>")
        await asyncio.sleep(DA_POLL_INTERVAL)

async def _run_ws_with_keeper():
    await asyncio.gather(listen_donations(), _token_keeper())

def _run_da():
    loop = asyncio.new_event_loop()
    asyncio.set_event_loop(loop)
    loop.run_until_complete(_run_ws_with_keeper())

def _run_rest():
    loop = asyncio.new_event_loop()
    asyncio.set_event_loop(loop)
    loop.run_until_complete(poll_donations_rest())

if __name__ == "__main__":
    log.info("Lichess Bot starting...")
    log.info(f"Admins: {ADMIN_IDS}")

    _cleanup_expired()
    _save_data(data)

    threading.Thread(target=_run_da, daemon=True).start()
    log.info("DonationAlerts WebSocket listener started")

    threading.Thread(target=_run_rest, daemon=True).start()
    log.info(f"DonationAlerts REST poller started (interval={DA_POLL_INTERVAL}s)")

    _exp = _parse_jwt_exp(DONATIONALERTS_TOKEN)
    _exp_str = time.strftime('%Y-%m-%d', time.localtime(_exp)) if _exp else 'unknown'
    _scopes = _token_scopes(DONATIONALERTS_TOKEN)
    _missing = [s for s in ("oauth-donation-subscribe", "oauth-donation-index", "oauth-user-show")
                if s not in _scopes]
    _notify_admins(
        f"🟢 <b>Bot started.</b> DA WebSocket + REST poller running. Auto-match enabled.\n"
        f"Proxy: <code>{DA_PROXY or 'none (trust_env=False)'}</code>\n"
        f"DA token valid until: <code>{_exp_str}</code> (auto-refresh on)\n"
        f"Scopes: <code>{' '.join(_scopes) or '?'}</code>"
        + (f"\n\n⚠️ Missing scopes: <code>{' '.join(_missing)}</code>\n"
           f"REST poller won't work. Run <code>python da_auth.py</code> once to fix."
           if _missing else ""))

    bot.infinity_polling(timeout=30, long_polling_timeout=30)
