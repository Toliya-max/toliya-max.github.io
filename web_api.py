"""Web checkout API for the Lichess Bot landing page.

Exposes two endpoints consumed by the static site:

    POST /api/checkout   - create a checkout session, return payUrl and session ID
    GET  /api/status     - poll session by ID, get status and (once paid) the key

Sessions live in the shared telegram_bot ``data`` dict under ``web_checkouts``
so they survive restarts via ``bot_data.json``.

``payUrl`` is the donate_url with ``?amount=<rub>&sum=<rub>&message=WEB_<sid>``
appended, so the DonationAlerts form is pre-filled and the user only confirms
the payment.

Donation -> session matching (see ``try_match_web_checkout``):
  1. Code match: ``WEB_<sid>`` substring in the donation message.
  2. Fallback: unique pending session with equal amount whose creation is
     within FALLBACK_WINDOW seconds of the donation.
"""
from __future__ import annotations

import logging
import os
import re
import secrets
import time
from collections import deque
from typing import Any
from urllib.parse import urlencode, urlparse, urlunparse

_EMAIL_RE = re.compile(r"^[^\s@]+@[^\s@]+\.[^\s@]{2,}$")


def _is_valid_email(value: str) -> bool:
    if not value or len(value) > 200:
        return False
    return bool(_EMAIL_RE.match(value))

from aiohttp import web

log = logging.getLogger(__name__)

CHECKOUT_TTL = 60 * 60
PAID_TTL = 24 * 3600
FALLBACK_WINDOW = 45 * 60
FALLBACK_LIFO_WINDOW = 15 * 60
AMOUNT_MATCH_TOLERANCE = 0.02


PAY_CURRENCY = "USD"


def _pick_unique_cents(base_amount: int, checkouts: dict[str, Any]) -> float:
    now = time.time()
    active_amounts: set[float] = set()
    for sess in checkouts.values():
        if sess.get("status") != "pending":
            continue
        if now - sess.get("created_at", 0) > FALLBACK_WINDOW:
            continue
        try:
            active_amounts.add(round(float(sess.get("amount_expected", 0)), 2))
        except (TypeError, ValueError):
            continue
    for _ in range(50):
        cents = secrets.randbelow(99) + 1
        amount = round(base_amount + cents / 100.0, 2)
        if amount not in active_amounts:
            return amount
    return round(base_amount + (secrets.randbelow(99) + 1) / 100.0, 2)


def _build_pay_url(base: str, amount: int, sid: str) -> str:
    parsed = urlparse(base)
    params = urlencode({
        "amount": amount,
        "sum": amount,
        "currency": PAY_CURRENCY,
        "message": f"WEB_{sid}",
    })
    existing = parsed.query
    query = f"{existing}&{params}" if existing else params
    return urlunparse(parsed._replace(query=query))

_PROD_ORIGINS = {
    "https://lichess-bot-controller.netlify.app",
    "https://chessbot.pages.dev",
    "https://toliya-max.github.io",
}
_DEV_ORIGINS = {
    "http://localhost:8080",
    "http://127.0.0.1:8080",
}


def _allowed_origins() -> set[str]:
    if os.environ.get("WEB_API_ALLOW_DEV_ORIGINS", "").strip().lower() in {"1", "true", "yes"}:
        return _PROD_ORIGINS | _DEV_ORIGINS
    return _PROD_ORIGINS


def _cors_headers(origin: str | None) -> dict[str, str]:
    headers: dict[str, str] = {
        "Access-Control-Allow-Methods": "GET, POST, OPTIONS",
        "Access-Control-Allow-Headers": "Content-Type",
        "Access-Control-Max-Age": "86400",
        "Vary": "Origin",
    }
    if origin and origin in _allowed_origins():
        headers["Access-Control-Allow-Origin"] = origin
    return headers


_CHECKOUT_RATE_LIMIT_MAX = 10
_CHECKOUT_RATE_LIMIT_WINDOW = 60
_checkout_rate_buckets: dict[str, deque[float]] = {}


def _client_ip(request: web.Request) -> str:
    xff = request.headers.get("X-Forwarded-For", "")
    if xff:
        return xff.split(",")[0].strip()
    return request.remote or "unknown"


def _check_rate_limit(bucket_key: str) -> bool:
    now = time.time()
    bucket = _checkout_rate_buckets.get(bucket_key)
    if bucket is None:
        bucket = deque()
        _checkout_rate_buckets[bucket_key] = bucket
    while bucket and now - bucket[0] > _CHECKOUT_RATE_LIMIT_WINDOW:
        bucket.popleft()
    if len(bucket) >= _CHECKOUT_RATE_LIMIT_MAX:
        return False
    bucket.append(now)
    if len(_checkout_rate_buckets) > 10000:
        for stale_key in list(_checkout_rate_buckets.keys())[:5000]:
            _checkout_rate_buckets.pop(stale_key, None)
    return True


def _mask_email(email: str) -> str:
    if not email or "@" not in email:
        return "-"
    local, _, domain = email.partition("@")
    if len(local) <= 2:
        return f"{local[:1]}***@{domain}"
    return f"{local[:2]}***@{domain}"


def _json(data: dict[str, Any], request: web.Request, status: int = 200) -> web.Response:
    return web.json_response(data, status=status, headers=_cors_headers(request.headers.get("Origin")))


async def _options(request: web.Request) -> web.Response:
    return web.Response(status=204, headers=_cors_headers(request.headers.get("Origin")))


def make_app(
    *,
    data: dict[str, Any],
    prices: dict[str, dict[str, Any]],
    donate_url: str,
    save_data,
) -> web.Application:
    """Build the aiohttp app bound to telegram_bot state."""

    def _cleanup_sessions() -> None:
        now = time.time()
        checkouts = data.setdefault("web_checkouts", {})
        stale: list[str] = []
        for sid, sess in checkouts.items():
            status = sess.get("status")
            created = sess.get("created_at", 0)
            age = now - created
            if status == "pending" and age > CHECKOUT_TTL:
                stale.append(sid)
            elif status == "paid" and age > PAID_TTL:
                stale.append(sid)
        for sid in stale:
            del checkouts[sid]
        if stale:
            save_data(data)

    async def checkout(request: web.Request) -> web.Response:
        _cleanup_sessions()

        if not _check_rate_limit(_client_ip(request)):
            return _json({"error": "rate limit exceeded", "message": "Too many checkout attempts. Please wait a minute and try again."}, request, 429)

        try:
            payload = await request.json()
        except Exception:
            return _json({"error": "invalid json"}, request, 400)

        tier = (payload.get("tier") or "").strip()
        email = (payload.get("email") or "").strip()[:200]

        if tier not in prices:
            return _json({"error": "unknown tier", "valid": list(prices)}, request, 400)

        if not _is_valid_email(email):
            return _json({"error": "email required", "message": "Please provide a valid email to receive your license key."}, request, 400)

        sid = secrets.token_urlsafe(8).replace("-", "").replace("_", "")[:10].upper()
        base_amount = prices[tier]["amount"]
        now = time.time()

        checkouts = data.setdefault("web_checkouts", {})
        amount = _pick_unique_cents(base_amount, checkouts)
        checkouts[sid] = {
            "created_at": now,
            "tier": tier,
            "amount_expected": amount,
            "email": email,
            "status": "pending",
            "key": None,
            "donation_id": None,
        }
        save_data(data)

        log.info(f"[WEB] checkout created sid={sid} tier={tier} amount={amount} email={_mask_email(email)}")

        pay_url = _build_pay_url(donate_url, amount, sid)

        return _json(
            {
                "sessionId": sid,
                "code": f"WEB_{sid}",
                "payUrl": pay_url,
                "amount": amount,
                "currency": PAY_CURRENCY,
                "tier": tier,
                "label": prices[tier]["label"],
                "days": prices[tier]["days"],
                "expiresIn": CHECKOUT_TTL,
            },
            request,
        )

    async def status(request: web.Request) -> web.Response:
        _cleanup_sessions()
        sid = (request.query.get("session") or "").strip().upper()
        if not sid:
            return _json({"error": "missing session"}, request, 400)

        checkouts = data.get("web_checkouts", {})
        sess = checkouts.get(sid)
        if not sess:
            return _json({"status": "expired"}, request, 404)

        return _json(
            {
                "sessionId": sid,
                "status": sess.get("status"),
                "tier": sess.get("tier"),
                "amountExpected": sess.get("amount_expected"),
                "key": sess.get("key") if sess.get("status") == "paid" else None,
                "createdAt": sess.get("created_at"),
            },
            request,
        )

    async def health(request: web.Request) -> web.Response:
        return _json({"ok": True, "service": "lichess-bot-checkout"}, request)

    import os as _os
    from pathlib import Path as _Path
    SITE_DIR = _Path(_os.path.dirname(_os.path.abspath(__file__))) / "site"

    async def site_index(request: web.Request) -> web.Response:
        index = SITE_DIR / "index.html"
        if not index.exists():
            return web.Response(text="site not deployed", status=503)
        html = index.read_text(encoding="utf-8")
        return web.Response(
            text=html,
            content_type="text/html",
            charset="utf-8",
            headers={
                "Cache-Control": "no-cache, must-revalidate",
                "X-Content-Type-Options": "nosniff",
                "Referrer-Policy": "strict-origin-when-cross-origin",
            },
        )

    app = web.Application()
    app.router.add_post("/api/checkout", checkout)
    app.router.add_get("/api/status", status)
    app.router.add_get("/api/health", health)
    app.router.add_options("/api/{tail:.*}", _options)
    app.router.add_get("/", site_index)
    if SITE_DIR.exists():
        app.router.add_static("/", path=str(SITE_DIR), show_index=False, follow_symlinks=False)
    return app


def _find_session_by_code(msg: str, checkouts: dict[str, Any]) -> tuple[str | None, dict | None]:
    import re as _re
    m = _re.search(r"WEB[_\s-]?([A-Z0-9]{6,12})", msg)
    if not m:
        return None, None
    sid = m.group(1)
    sess = checkouts.get(sid)
    return sid, sess


def _find_session_by_fallback(
    donation: dict[str, Any],
    checkouts: dict[str, Any],
) -> tuple[str | None, dict | None]:
    """Fallback matcher: find a pending session by amount + time window.

    Used when donation message does not contain the WEB_<sid> code (common:
    DonationAlerts sometimes strips the ?message= URL parameter).

    Rules:
      - status must be 'pending'
      - amount_expected must equal paid amount (+/- 1 USD)
      - session created within FALLBACK_WINDOW seconds of the donation
      - if exactly one candidate -> match
      - if multiple candidates -> pick the most recently created one
        (LIFO) inside FALLBACK_LIFO_WINDOW, otherwise skip to stay safe.
    """
    try:
        paid_amount = float(donation.get("amount", 0))
    except (TypeError, ValueError):
        return None, None
    if paid_amount <= 0:
        return None, None

    now = time.time()
    candidates: list[tuple[str, dict[str, Any]]] = []
    for sid, sess in checkouts.items():
        if sess.get("status") != "pending":
            continue
        try:
            expected = float(sess.get("amount_expected", 0))
        except (TypeError, ValueError):
            continue
        if abs(paid_amount - expected) > AMOUNT_MATCH_TOLERANCE:
            continue
        created = sess.get("created_at", 0)
        if now - created > FALLBACK_WINDOW:
            continue
        candidates.append((sid, sess))

    if not candidates:
        return None, None

    if len(candidates) == 1:
        sid, sess = candidates[0]
        log.info(f"[WEB] fallback matched donation {donation.get('id')} to session {sid} by amount+time (unique)")
        return sid, sess

    candidates.sort(key=lambda kv: kv[1].get("created_at", 0), reverse=True)
    newest_sid, newest_sess = candidates[0]
    newest_age = now - newest_sess.get("created_at", 0)
    if newest_age <= FALLBACK_LIFO_WINDOW:
        log.info(
            f"[WEB] fallback LIFO-matched donation {donation.get('id')} to newest session {newest_sid} "
            f"(age={int(newest_age)}s, {len(candidates)} candidates)"
        )
        return newest_sid, newest_sess

    log.warning(
        f"[WEB] fallback matcher found {len(candidates)} candidates for "
        f"donation {donation.get('id')} amount={paid_amount}, newest age={int(newest_age)}s — skipping (ambiguous)"
    )
    return None, None


def try_match_web_checkout(
    donation: dict[str, Any],
    *,
    data: dict[str, Any],
    prices: dict[str, dict[str, Any]],
    generate_key,
    save_data,
) -> tuple[bool, str | None, dict | None]:
    """Match a donation message against a pending web-checkout session.

    Returns (matched, key, session_dict). When matched, the session is marked
    paid and the generated key is stored. Idempotent: a second matching
    donation is a no-op and just returns the existing key.

    Two matching strategies, in order:
      1. Code match: WEB_<sid> substring in the donation message.
      2. Fallback match: unique pending session with equal amount within
         FALLBACK_WINDOW seconds of creation.
    """
    msg = (donation.get("message") or "").strip().upper()
    donation_currency = (donation.get("currency") or "").strip().upper()
    checkouts = data.setdefault("web_checkouts", {})

    if donation_currency and donation_currency != PAY_CURRENCY:
        log.warning(
            f"[WEB] donation {donation.get('id')} currency={donation_currency} "
            f"does not match expected {PAY_CURRENCY}; skipping match"
        )
        return False, None, None

    sid, sess = _find_session_by_code(msg, checkouts)

    if sess is None:
        sid, sess = _find_session_by_fallback(donation, checkouts)
        if sess is None:
            if "WEB_" in msg and sid:
                log.warning(f"[WEB] donation {donation.get('id')} references unknown session {sid}")
            return False, None, None

    if sess.get("status") == "paid" and sess.get("key"):
        log.info(f"[WEB] donation {donation.get('id')} re-matches already-paid session {sid}")
        return True, sess["key"], sess

    try:
        paid_amount = float(donation.get("amount", 0))
    except (TypeError, ValueError):
        paid_amount = 0.0
    try:
        expected = float(sess.get("amount_expected", 0))
    except (TypeError, ValueError):
        expected = 0.0
    if paid_amount + AMOUNT_MATCH_TOLERANCE < expected:
        log.warning(
            f"[WEB] donation {donation.get('id')} underpaid for session {sid}: "
            f"got {paid_amount:.2f} {PAY_CURRENCY}, expected {expected:.2f} {PAY_CURRENCY}"
        )
        return False, None, None

    tier = sess.get("tier")
    if tier not in prices:
        log.error(f"[WEB] session {sid} has invalid tier {tier}")
        return False, None, None

    key_type_map = {
        "1day": "1day",
        "7days": "7days",
        "monthly": "monthly",
        "3months": "3months",
        "yearly": "yearly",
    }
    kt = key_type_map.get(tier)
    if not kt:
        return False, None, None

    try:
        key = generate_key(kt)
    except Exception as e:
        log.exception(f"[WEB] key generation failed for session {sid}: {e}")
        return False, None, None

    sess["status"] = "paid"
    sess["key"] = key
    sess["donation_id"] = donation.get("id")
    sess["paid_at"] = time.time()
    save_data(data)

    log.info(f"[WEB] session {sid} paid by donation {donation.get('id')}, key issued ({kt})")

    email = (sess.get("email") or "").strip()
    if email:
        try:
            import email_utils
            ok = email_utils.send_key_email(
                to=email,
                key=key,
                tier_label=prices[tier]["label"],
                days=prices[tier]["days"],
                amount=paid_amount,
                session_id=sid,
            )
            sess["email_sent"] = bool(ok)
            sess["email_sent_at"] = time.time() if ok else None
            save_data(data)
        except Exception as e:
            log.exception(f"[WEB] email dispatch failed for session {sid}: {e}")
            sess["email_sent"] = False
            save_data(data)

    return True, key, sess


def run_server(app: web.Application, host: str = "127.0.0.1", port: int = 8383) -> None:
    """Run the aiohttp app on its own event loop (blocking)."""
    import asyncio

    loop = asyncio.new_event_loop()
    asyncio.set_event_loop(loop)

    runner = web.AppRunner(app, keepalive_timeout=75, handler_cancellation=False)
    loop.run_until_complete(runner.setup())
    site = web.TCPSite(runner, host, port, reuse_address=True)
    loop.run_until_complete(site.start())
    log.info(f"[WEB] checkout API listening on http://{host}:{port}")
    try:
        loop.run_forever()
    except KeyboardInterrupt:
        pass
    finally:
        loop.run_until_complete(runner.cleanup())
        loop.close()
