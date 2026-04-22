"""SMTP delivery for license keys issued via on-site checkout.

Reads config from environment:
    SMTP_HOST, SMTP_PORT, SMTP_USER, SMTP_PASS, SMTP_FROM, SMTP_TLS

All failures are logged and swallowed — the key is still stored in the
session so the site polling flow recovers it. Email is a backup channel.
"""
from __future__ import annotations

import logging
import os
import smtplib
import ssl
from email.message import EmailMessage

log = logging.getLogger(__name__)


def _cfg() -> dict[str, str]:
    return {
        "host": (os.environ.get("SMTP_HOST") or "").strip(),
        "port": (os.environ.get("SMTP_PORT") or "").strip(),
        "user": (os.environ.get("SMTP_USER") or "").strip(),
        "password": os.environ.get("SMTP_PASS") or "",
        "from_": (os.environ.get("SMTP_FROM") or os.environ.get("SMTP_USER") or "").strip(),
        "tls_mode": (os.environ.get("SMTP_TLS") or "ssl").strip().lower(),
    }


def is_configured() -> bool:
    c = _cfg()
    return bool(c["host"] and c["port"] and c["user"] and c["password"] and c["from_"])


def send_key_email(
    *,
    to: str,
    key: str,
    tier_label: str,
    days: int,
    amount: float,
    session_id: str,
) -> bool:
    """Send a license key by email. Returns True on success."""
    if not to:
        return False
    c = _cfg()
    if not is_configured():
        log.warning(f"[EMAIL] SMTP not configured; skipping send to {to}")
        return False

    subject = f"Your Lichess Bot Controller license — {tier_label}"
    body_text = f"""Hi,

Thanks for your purchase. Here is your license key:

    {key}

Plan:     {tier_label} ({days} days)
Amount:   ${amount:.2f} USD
Order:    WEB_{session_id}

How to activate:
  1. Download the installer: https://lichess-bot-controller.netlify.app/downloads/LichessBotSetup.exe
  2. Run it, then paste the key when prompted.

Keep this email — the key is the only proof of purchase.

If something went wrong, reply to this email or open the Telegram bot:
https://t.me/LichessBotDownoloaderbot

— Lichess Bot Controller
"""

    body_html = f"""<!doctype html>
<html><body style="font-family:-apple-system,Segoe UI,Arial,sans-serif;color:#111;max-width:600px;margin:0 auto;padding:24px">
  <h2 style="margin:0 0 16px;color:#0e0b07">Your license key</h2>
  <p style="margin:0 0 12px">Thanks for your purchase. Here is your key:</p>
  <pre style="background:#f7f3ea;border:1px solid #e4ddcb;border-radius:8px;padding:16px;font-size:18px;font-family:JetBrains Mono,Consolas,monospace;word-break:break-all">{key}</pre>
  <table style="border-collapse:collapse;margin:16px 0;font-size:14px">
    <tr><td style="padding:4px 16px 4px 0;color:#666">Plan</td><td style="padding:4px 0"><b>{tier_label}</b> ({days} days)</td></tr>
    <tr><td style="padding:4px 16px 4px 0;color:#666">Amount</td><td style="padding:4px 0">${amount:.2f} USD</td></tr>
    <tr><td style="padding:4px 16px 4px 0;color:#666">Order</td><td style="padding:4px 0"><code>WEB_{session_id}</code></td></tr>
  </table>
  <ol style="padding-left:20px;margin:16px 0">
    <li>Download the installer: <a href="https://lichess-bot-controller.netlify.app/downloads/LichessBotSetup.exe">LichessBotSetup.exe</a></li>
    <li>Run it, then paste the key when prompted.</li>
  </ol>
  <p style="margin-top:24px;padding-top:16px;border-top:1px solid #eee;font-size:13px;color:#666">
    Keep this email — the key is the only proof of purchase.<br>
    Questions? Reply here or <a href="https://t.me/LichessBotDownoloaderbot">open the Telegram bot</a>.
  </p>
</body></html>
"""

    msg = EmailMessage()
    msg["Subject"] = subject
    msg["From"] = c["from_"]
    msg["To"] = to
    msg.set_content(body_text)
    msg.add_alternative(body_html, subtype="html")

    try:
        port = int(c["port"])
    except ValueError:
        log.error(f"[EMAIL] invalid SMTP_PORT={c['port']!r}")
        return False

    try:
        if c["tls_mode"] == "starttls":
            with smtplib.SMTP(c["host"], port, timeout=30) as s:
                s.ehlo()
                s.starttls(context=ssl.create_default_context())
                s.ehlo()
                s.login(c["user"], c["password"])
                s.send_message(msg)
        else:
            with smtplib.SMTP_SSL(c["host"], port, context=ssl.create_default_context(), timeout=30) as s:
                s.login(c["user"], c["password"])
                s.send_message(msg)
        log.info(f"[EMAIL] key sent to {to} (session WEB_{session_id})")
        return True
    except Exception as e:
        log.exception(f"[EMAIL] send to {to} failed: {e}")
        return False
