"""
License validation module for Lichess Bot.
Keys are self-contained (HMAC-signed), stored locally with Fernet encryption.
Online check fetches a revocation list from a GitHub Gist.
"""
import os
import sys
import json
import hmac
import hashlib
import base64
import struct
import datetime
import urllib.request
import urllib.error

try:
    from cryptography.fernet import Fernet, InvalidToken
    _FERNET_AVAILABLE = True
except ImportError:
    _FERNET_AVAILABLE = False

# ─── constants ────────────────────────────────────────────────────────────────

# HMAC secret — change this before distributing; keep it private.
_HMAC_SECRET = b"L1ch355B0t$3cr3tK3y#2025!xQ7"

# Fernet key used to encrypt license.dat on disk.
# Must be 32 url-safe base64-encoded bytes.
_FERNET_KEY = b"dGhpcyBpcyBhIDMyLWJ5dGUga2V5IS4="  # placeholder — 32 bytes

# GitHub Gist raw URL with a JSON array of revoked key hashes.
# Example gist content: ["aabbccdd...", "11223344..."]
_REVOCATION_URL = (
    "https://gist.githubusercontent.com/Toliya-max/"
    "lichess_revoked_keys/raw/revoked.json"
)

_LICENSE_FILENAME = "license.dat"

# ─── key structure ────────────────────────────────────────────────────────────
# Raw key bytes (before base32 + hyphen formatting):
#   1 byte  — type: 0x4D ('M') = monthly, 0x59 ('Y') = yearly
#   4 bytes — expiry as Unix timestamp (uint32, big-endian)
#   8 bytes — first 8 bytes of HMAC-SHA256 signature
# Total: 13 bytes → 24 base32 chars (no padding) → grouped as XXXX-XXXX-XXXX-XXXX-XXXX (20 chars + 4 hyphens)

_KEY_BYTE_LEN = 13   # 1 + 4 + 8
_B32_UNPADDED_LEN = 24  # ceil(13*8/5) = 21, but base32 pads to nearest 8: 24

def _compute_sig(key_type: int, expiry: int) -> bytes:
    payload = struct.pack(">BI", key_type, expiry)
    full = hmac.new(_HMAC_SECRET, payload, hashlib.sha256).digest()
    return full[:8]

def _encode_key(key_type: int, expiry: int) -> str:
    sig = _compute_sig(key_type, expiry)
    raw = struct.pack(">BI", key_type, expiry) + sig
    b32 = base64.b32encode(raw).decode().rstrip("=")
    # pad to 24 chars (should already be 24 for 13 bytes)
    b32 = b32.ljust(24, "A")
    # group as 5 groups of 4 chars, but we have 24 chars: 6 groups of 4
    groups = [b32[i:i+4] for i in range(0, len(b32), 4)]
    return "-".join(groups)

def _decode_key(key_str: str):
    """
    Returns (key_type: int, expiry: datetime) or raises ValueError.
    """
    cleaned = key_str.strip().upper().replace("-", "").replace(" ", "")
    if len(cleaned) != 24:
        raise ValueError("Invalid key length")
    padded = cleaned + "=" * ((8 - len(cleaned) % 8) % 8)
    try:
        raw = base64.b32decode(padded)
    except Exception:
        raise ValueError("Invalid key encoding")
    if len(raw) < 13:
        raise ValueError("Key too short")

    key_type = raw[0]
    expiry_ts = struct.unpack(">I", raw[1:5])[0]
    stored_sig = raw[5:13]

    expected_sig = _compute_sig(key_type, expiry_ts)
    if not hmac.compare_digest(stored_sig, expected_sig):
        raise ValueError("Key signature invalid")

    if key_type not in (ord("M"), ord("Y")):
        raise ValueError("Unknown key type")

    expiry_dt = datetime.datetime.utcfromtimestamp(expiry_ts)
    return key_type, expiry_dt

# ─── license file path ────────────────────────────────────────────────────────

def _license_path() -> str:
    exe_dir = os.path.dirname(os.path.abspath(sys.argv[0]))
    return os.path.join(exe_dir, _LICENSE_FILENAME)

# ─── fernet helpers ───────────────────────────────────────────────────────────

def _make_fernet():
    if not _FERNET_AVAILABLE:
        return None
    key = _FERNET_KEY + b"=" * ((4 - len(_FERNET_KEY) % 4) % 4)
    try:
        return Fernet(key)
    except Exception:
        return None

def _save_license(key_str: str):
    path = _license_path()
    data = key_str.encode()
    f = _make_fernet()
    if f:
        data = f.encrypt(data)
    else:
        data = base64.b64encode(data)
    with open(path, "wb") as fp:
        fp.write(data)

def _load_license() -> str:
    path = _license_path()
    if not os.path.exists(path):
        return ""
    with open(path, "rb") as fp:
        data = fp.read()
    f = _make_fernet()
    if f:
        try:
            data = f.decrypt(data)
        except (InvalidToken, Exception):
            # Fallback: try base64 (old format or Fernet unavailable at write time)
            try:
                data = base64.b64decode(data)
            except Exception:
                return ""
    else:
        try:
            data = base64.b64decode(data)
        except Exception:
            return ""
    return data.decode(errors="replace").strip()

# ─── revocation check ─────────────────────────────────────────────────────────

def _key_hash(key_str: str) -> str:
    cleaned = key_str.strip().upper().replace("-", "")
    return hashlib.sha256(cleaned.encode()).hexdigest()[:16]

def _is_revoked(key_str: str) -> bool:
    try:
        req = urllib.request.Request(
            _REVOCATION_URL,
            headers={"User-Agent": "LichessBot/1.0"},
        )
        with urllib.request.urlopen(req, timeout=5) as resp:
            revoked = json.loads(resp.read().decode())
        return _key_hash(key_str) in revoked
    except Exception:
        # Network unavailable — allow (don't block offline users)
        return False

# ─── public API ───────────────────────────────────────────────────────────────

class LicenseError(Exception):
    pass

def activate(key_str: str) -> dict:
    """
    Validate key_str, save to disk if valid.
    Returns info dict with 'type', 'expiry', 'days_left'.
    Raises LicenseError on failure.
    """
    try:
        key_type, expiry = _decode_key(key_str)
    except ValueError as e:
        raise LicenseError(str(e))

    now = datetime.datetime.utcnow()
    if expiry < now:
        raise LicenseError("License key has expired")

    if _is_revoked(key_str):
        raise LicenseError("License key has been revoked")

    _save_license(key_str)
    days_left = (expiry - now).days
    return {
        "type": "Monthly" if key_type == ord("M") else "Yearly",
        "expiry": expiry.strftime("%Y-%m-%d"),
        "days_left": days_left,
    }

def check() -> dict:
    """
    Load and validate the stored license.
    Returns info dict or raises LicenseError.
    """
    key_str = _load_license()
    if not key_str:
        raise LicenseError("No license key found")

    try:
        key_type, expiry = _decode_key(key_str)
    except ValueError as e:
        raise LicenseError(f"Stored key is corrupt: {e}")

    now = datetime.datetime.utcnow()
    if expiry < now:
        raise LicenseError(
            f"License expired on {expiry.strftime('%Y-%m-%d')}. "
            "Please renew your subscription."
        )

    days_left = (expiry - now).days
    return {
        "type": "Monthly" if key_type == ord("M") else "Yearly",
        "expiry": expiry.strftime("%Y-%m-%d"),
        "days_left": days_left,
        "key": key_str,
    }

def deactivate():
    """Remove stored license."""
    path = _license_path()
    if os.path.exists(path):
        os.remove(path)
