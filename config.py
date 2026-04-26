import os
from dotenv import load_dotenv

# Load variables from .env file
load_dotenv()

LICHESS_API_TOKEN = os.getenv("LICHESS_API_TOKEN")

# Resolve all paths relative to THIS file's directory (D:\lichess)
_BASE_DIR = os.path.dirname(os.path.abspath(__file__))

_default_engine = os.path.join(_BASE_DIR, "stockfish18", "stockfish-windows-x86-64-avx2.exe")
_env_engine = os.getenv("STOCKFISH_PATH")
if _env_engine:
    # If .env gives a relative path, resolve it relative to project root
    STOCKFISH_PATH = _env_engine if os.path.isabs(_env_engine) else os.path.join(_BASE_DIR, _env_engine)
else:
    STOCKFISH_PATH = _default_engine

_env_book = os.getenv("BOOK_PATH")
if _env_book:
    BOOK_PATH = _env_book if os.path.isabs(_env_book) else os.path.join(_BASE_DIR, _env_book)
else:
    BOOK_PATH = os.path.join(_BASE_DIR, "gm_openings.bin")

if not LICHESS_API_TOKEN:
    print("WARNING: LICHESS_API_TOKEN is not set in .env. The bot will not be able to connect.")

if not os.path.exists(STOCKFISH_PATH):
    print(f"WARNING: Stockfish executable not found at {STOCKFISH_PATH}.")

import multiprocessing
import ctypes

def get_optimal_threads():
    # Leave 1 thread for the OS + Python bot process; use all else for Stockfish.
    count = multiprocessing.cpu_count()
    return max(1, count - 1)

def get_optimal_hash():
    # Allocate ~33% of total system RAM for the engine hash table.
    # More hash = fewer transposition collisions = stronger play at all depths.
    # Minimum 256 MB, maximum 32768 MB (Stockfish's practical ceiling).
    try:
        class MEMORYSTATUSEX(ctypes.Structure):
            _fields_ = [
                ("dwLength", ctypes.c_ulong),
                ("dwMemoryLoad", ctypes.c_ulong),
                ("ullTotalPhys", ctypes.c_ulonglong),
                ("ullAvailPhys", ctypes.c_ulonglong),
                ("ullTotalPageFile", ctypes.c_ulonglong),
                ("ullAvailPageFile", ctypes.c_ulonglong),
                ("ullTotalVirtual", ctypes.c_ulonglong),
                ("ullAvailVirtual", ctypes.c_ulonglong),
                ("sullAvailExtendedVirtual", ctypes.c_ulonglong),
            ]

        stat = MEMORYSTATUSEX()
        stat.dwLength = ctypes.sizeof(MEMORYSTATUSEX)
        ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(stat))

        # Total physical memory in MB
        total_mb = stat.ullTotalPhys / (1024 * 1024)

        # Take 33% of RAM for hash — more than before for maximum strength.
        hash_mb = int(total_mb * 0.33)
        return max(256, min(32768, hash_mb))
    except Exception:
        # Fallback if Windows API fails
        return 2048

# Calculate optimal hardware settings once
OPT_THREADS = get_optimal_threads()
OPT_HASH = get_optimal_hash()

ENGINES = {
    # Fully unleashed: no Skill Level cap, all strength options enabled.
    "Stockfish_Max": {
        "path": STOCKFISH_PATH,
        "options": {
            "Threads": OPT_THREADS,
            "Hash": OPT_HASH,
            "UCI_LimitStrength": False,
            "Move Overhead": 30,
            "Ponder": False,
        }
    },
    # Same full strength but one fewer thread reserved for OS headroom.
    "Stockfish_Tactical": {
        "path": STOCKFISH_PATH,
        "options": {
            "Threads": max(1, OPT_THREADS - 1),
            "Hash": OPT_HASH,
            "UCI_LimitStrength": False,
            "Move Overhead": 30,
            "Ponder": False,
        }
    },
    # Reduced-strength profile for handicapped games.
    "Stockfish_Fast": {
        "path": STOCKFISH_PATH,
        "options": {
            "Skill Level": 15,
            "Threads": max(1, int(OPT_THREADS / 2)),
            "Hash": max(128, int(OPT_HASH / 2)),
            "UCI_LimitStrength": True,
            "Move Overhead": 100,
        }
    }
}
