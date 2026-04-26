"""Thread-safe live configuration for the running bot.

The C# GUI (or any local client) can mutate these values at runtime via
``POST /config`` on the eval-server (port 8282). All consumers read through
``get()`` / ``snapshot()``; subscribers receive an ``apply()`` callback with
the changed keys so they can push relevant updates to the engine, challenger
loop, chat module, etc., without restarting the process.

Keys are typed loosely on purpose — the GUI sends raw JSON and the bot
coerces values where needed.
"""
from __future__ import annotations

import threading
from typing import Any, Callable

_lock = threading.RLock()
_state: dict[str, Any] = {}
_subscribers: list[Callable[[dict[str, Any], dict[str, Any]], None]] = []


def init(initial: dict[str, Any]) -> None:
    """Seed config from CLI args once at startup. Does not fire subscribers."""
    with _lock:
        _state.clear()
        _state.update(initial)


def get(key: str, default: Any = None) -> Any:
    with _lock:
        return _state.get(key, default)


def snapshot() -> dict[str, Any]:
    with _lock:
        return dict(_state)


def update(changes: dict[str, Any]) -> dict[str, Any]:
    """Merge ``changes`` into state. Returns the diff actually applied
    (only keys whose value changed). Fires subscribers outside the lock.
    """
    diff: dict[str, Any] = {}
    with _lock:
        for k, v in changes.items():
            if _state.get(k) != v:
                _state[k] = v
                diff[k] = v
        snap = dict(_state)
    if diff:
        for cb in list(_subscribers):
            try:
                cb(diff, snap)
            except Exception:
                import logging
                logging.getLogger(__name__).exception("live_config subscriber failed")
    return diff


def subscribe(cb: Callable[[dict[str, Any], dict[str, Any]], None]) -> None:
    with _lock:
        if cb not in _subscribers:
            _subscribers.append(cb)


def unsubscribe(cb: Callable[[dict[str, Any], dict[str, Any]], None]) -> None:
    with _lock:
        if cb in _subscribers:
            _subscribers.remove(cb)
