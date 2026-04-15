import os
import sys
import json
import hmac
import hashlib
import base64
import struct
import secrets
import datetime
import urllib.request
import urllib.error

try:
    from cryptography.fernet import Fernet, InvalidToken
    _FERNET_AVAILABLE = True
except ImportError:
    _FERNET_AVAILABLE = False


def _xd(data: bytes, m: bytes) -> bytes:
    return bytes(b ^ m[i % len(m)] for i, b in enumerate(data))

_M = bytes([
    0x5a, 0x3f, 0x7c, 0x11, 0x88, 0xd2, 0x44, 0xab,
    0x9e, 0x61, 0x23, 0x57, 0xf0, 0x04, 0xbc, 0x77,
    0x31, 0xca, 0x09, 0x5b, 0xe8, 0x6f, 0xd3, 0x18,
    0xa4, 0x72, 0xb6, 0x4d, 0x0c, 0x93, 0x2e, 0xf5,
])

_HS_ENC = bytes([
    58,  3, 244, 136, 208,  32, 225, 123,
   254,  23, 180, 147,   6,  36,  28, 109,
    95, 164, 201,  34,  77,  41,  59, 155,
    69,  59, 253, 148, 252, 221,  55, 234,
])
_HMAC_SECRET: bytes = _xd(_HS_ENC, _M)

_FK_ENC = bytes([
    35,  80,  81, 100, 196, 148,  18, 221,
   243,  27,  18,  61, 194,  96, 214,  29,
   115, 135,  71,  58, 165,  31, 181, 118,
   214,  10, 198,  38, 105, 208, 100, 153,
    45,  88,  44,  66, 220, 235, 118, 210,
   201,  80,  76, 106,
])
_FERNET_KEY: bytes = _xd(_FK_ENC, _M)

_REVOCATION_URL = (
    "https://gist.githubusercontent.com/Toliya-max/"
    "lichess_revoked_keys/raw/revoked.json"
)

_LICENSE_FILENAME = "license.dat"

_KEY_VERSION = 0x02
_KEY_BYTE_LEN = 24
_B32_LEN = 40


def _compute_sig_v2(payload9: bytes) -> bytes:
    return hmac.new(_HMAC_SECRET, payload9, hashlib.sha256).digest()[:14]

def _encode_key(key_type: int, expiry_ts: int, nonce: bytes | None = None) -> str:
    if nonce is None:
        nonce = secrets.token_bytes(4)
    header = struct.pack(">BBI", _KEY_VERSION, key_type, expiry_ts) + nonce
    sig = _compute_sig_v2(header)
    raw = header + sig
    b32 = base64.b32encode(raw).decode().rstrip("=")
    b32 = b32.ljust(_B32_LEN, "A")
    groups = [b32[i:i+5] for i in range(0, _B32_LEN, 5)]
    return "-".join(groups)

def _decode_key(key_str: str):
    cleaned = key_str.strip().upper().replace("-", "").replace(" ", "")
    if len(cleaned) != _B32_LEN:
        raise ValueError("Invalid key length")
    padded = cleaned + "=" * ((8 - len(cleaned) % 8) % 8)
    try:
        raw = base64.b32decode(padded)
    except Exception:
        raise ValueError("Invalid key encoding")
    if len(raw) < _KEY_BYTE_LEN:
        raise ValueError("Key too short")

    version = raw[0]
    if version != _KEY_VERSION:
        raise ValueError("Unsupported key version")

    key_type = raw[1]
    expiry_ts = struct.unpack(">I", raw[2:6])[0]
    nonce = raw[6:10]
    stored_sig = raw[10:24]

    header = raw[:10]
    expected_sig = _compute_sig_v2(header)
    if not hmac.compare_digest(stored_sig, expected_sig):
        raise ValueError("Key signature invalid")

    if key_type not in (ord("1"), ord("W"), ord("M"), ord("Q"), ord("Y"), ord("D")):
        raise ValueError("Unknown key type")

    expiry_dt = datetime.datetime.utcfromtimestamp(expiry_ts)
    return key_type, expiry_dt


def _get_machine_id() -> str:
    try:
        import winreg
        with winreg.OpenKey(
            winreg.HKEY_LOCAL_MACHINE,
            r"SOFTWARE\Microsoft\Cryptography",
        ) as k:
            guid, _ = winreg.QueryValueEx(k, "MachineGuid")
            return hashlib.sha256(guid.lower().encode()).hexdigest()[:32]
    except Exception:
        pass
    try:
        import socket
        return hashlib.sha256(socket.gethostname().encode()).hexdigest()[:32]
    except Exception:
        return "00000000000000000000000000000000"


def _license_path() -> str:
    exe_dir = os.path.dirname(os.path.abspath(sys.argv[0]))
    return os.path.join(exe_dir, _LICENSE_FILENAME)


def _make_fernet():
    if not _FERNET_AVAILABLE:
        return None
    try:
        return Fernet(_FERNET_KEY)
    except Exception:
        return None

def _save_license(key_str: str):
    machine_id = _get_machine_id()
    blob = f"{key_str}:{machine_id}".encode()
    f = _make_fernet()
    if f:
        data = f.encrypt(blob)
    else:
        data = base64.b64encode(blob)
    with open(_license_path(), "wb") as fp:
        fp.write(data)

def _load_license() -> tuple[str, str]:
    path = _license_path()
    if not os.path.exists(path):
        return "", ""
    with open(path, "rb") as fp:
        data = fp.read()
    f = _make_fernet()
    if f:
        try:
            data = f.decrypt(data)
        except (InvalidToken, Exception):
            try:
                data = base64.b64decode(data)
            except Exception:
                return "", ""
    else:
        try:
            data = base64.b64decode(data)
        except Exception:
            return "", ""
    text = data.decode(errors="replace").strip()
    if ":" in text:
        key_str, machine_id = text.rsplit(":", 1)
    else:
        key_str, machine_id = text, ""
    return key_str, machine_id


def _key_hash(key_str: str) -> str:
    cleaned = key_str.strip().upper().replace("-", "")
    return hashlib.sha256(cleaned.encode()).hexdigest()[:16]

def _is_revoked(key_str: str) -> bool:
    try:
        req = urllib.request.Request(
            _REVOCATION_URL,
            headers={"User-Agent": "LichessBot/2.0"},
        )
        with urllib.request.urlopen(req, timeout=5) as resp:
            revoked = json.loads(resp.read().decode())
        return _key_hash(key_str) in revoked
    except Exception:
        return False


class LicenseError(Exception):
    pass

def activate(key_str: str) -> dict:
    try:
        key_type, expiry = _decode_key(key_str)
    except ValueError as e:
        raise LicenseError(str(e))

    is_dev = key_type == ord("D")
    now = datetime.datetime.utcnow()
    if not is_dev and expiry < now:
        raise LicenseError("License key has expired")

    if _is_revoked(key_str):
        raise LicenseError("License key has been revoked")

    _save_license(key_str)

    days_left = -1 if is_dev else (expiry - now).days
    type_name = {ord("1"): "1 Day", ord("W"): "7 Days", ord("M"): "Monthly", ord("Q"): "3 Months", ord("Y"): "Yearly", ord("D"): "Developer"}[key_type]
    return {
        "type": type_name,
        "expiry": "never" if is_dev else expiry.strftime("%Y-%m-%d"),
        "days_left": days_left,
    }

def check() -> dict:
    key_str, saved_mid = _load_license()
    if not key_str:
        raise LicenseError("No license key found")

    current_mid = _get_machine_id()
    if saved_mid and current_mid != saved_mid:
        raise LicenseError(
            "License is bound to a different machine. "
            "Please reactivate with your license key."
        )

    try:
        key_type, expiry = _decode_key(key_str)
    except ValueError as e:
        raise LicenseError(f"Stored key is corrupt: {e}")

    is_dev = key_type == ord("D")
    now = datetime.datetime.utcnow()
    if not is_dev and expiry < now:
        raise LicenseError(
            f"License expired on {expiry.strftime('%Y-%m-%d')}. "
            "Please renew your subscription."
        )

    days_left = -1 if is_dev else (expiry - now).days
    type_name = {ord("1"): "1 Day", ord("W"): "7 Days", ord("M"): "Monthly", ord("Q"): "3 Months", ord("Y"): "Yearly", ord("D"): "Developer"}[key_type]
    return {
        "type": type_name,
        "expiry": "never" if is_dev else expiry.strftime("%Y-%m-%d"),
        "days_left": days_left,
        "key": key_str,
    }

def validate(key_str: str) -> dict:
    try:
        key_type, expiry = _decode_key(key_str)
    except ValueError as e:
        raise LicenseError(str(e))

    is_dev = key_type == ord("D")
    now = datetime.datetime.utcnow()
    if not is_dev and expiry < now:
        raise LicenseError("License key has expired")

    days_left = -1 if is_dev else (expiry - now).days
    type_name = {ord("1"): "1 Day", ord("W"): "7 Days", ord("M"): "Monthly", ord("Q"): "3 Months", ord("Y"): "Yearly", ord("D"): "Developer"}[key_type]
    return {
        "type": type_name,
        "expiry": "never" if is_dev else expiry.strftime("%Y-%m-%d"),
        "days_left": days_left,
    }

def deactivate():
    path = _license_path()
    if os.path.exists(path):
        os.remove(path)
