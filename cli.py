#!/usr/bin/env python
"""
CLI entry point for the Lichess Bot.
Designed to be launched by the C# GUI via Process.Start().
All settings are passed as command-line arguments.
stdout is unbuffered so the C# host can read logs in real time.
"""
import sys
import os
import argparse

# Force unbuffered stdout so C# reads logs instantly
sys.stdout.reconfigure(line_buffering=True, encoding='utf-8')
sys.stderr.reconfigure(line_buffering=True, encoding='utf-8')

def _check_license():
    try:
        import license as lic
        info = lic.check()
        print(f"[LICENSE] {info['type']} license active — expires {info['expiry']} ({info['days_left']} days left)")
    except ImportError:
        print("[LICENSE] ERROR: license module not found. Cannot run without license.")
        sys.exit(2)
    except Exception as e:
        print(f"[LICENSE] ERROR: {e}")
        sys.exit(2)


def main():
    if sys.gettrace() is not None:
        sys.exit(1)

    parser = argparse.ArgumentParser(description="Lichess Bot CLI")
    parser.add_argument("--token", type=str, default=None, help="Lichess API token (overrides .env)")
    parser.add_argument("--min-rating", type=int, default=1900, help="Minimum opponent rating")
    parser.add_argument("--max-games", type=int, default=0, help="Max games to play (0 = unlimited)")
    parser.add_argument("--skill", type=int, default=20, help="Stockfish Skill Level (0-20)")
    parser.add_argument("--depth", type=int, default=0, help="Max search depth (0 = unlimited)")
    parser.add_argument("--speed", type=float, default=1.0, help="Speed multiplier (0.1 - 3.0)")
    parser.add_argument("--rated", action="store_true", help="Send rated challenges (default: casual)")
    parser.add_argument("--no-challenger", action="store_true", help="Disable auto-challenger")
    parser.add_argument("--tc-minutes", type=float, default=2.0, help="Base time control in minutes")
    parser.add_argument("--tc-increment", type=int, default=1, help="Time control increment in seconds")
    parser.add_argument("--engine-path", type=str, default=None, help="Custom engine executable path")
    parser.add_argument("--book-path", type=str, default=None, help="Custom opening book (.bin) path")
    parser.add_argument("--no-nnue", action="store_true", help="Disable the NNUE neural network evaluation")
    parser.add_argument("--auto-resign", action="store_true", help="Automatically resign if engine evaluation drops below threshold")
    parser.add_argument("--resign-threshold", type=float, default=-5.0, help="Evaluation threshold to trigger resign (e.g. -5.0 pawns)")
    parser.add_argument("--threads", type=int, default=None, help="Engine threads (overrides config)")
    parser.add_argument("--hash", type=int, default=None, help="Engine hash size in MB (overrides config)")
    parser.add_argument("--no-chat", action="store_true", help="Disable chat messages (glhf/gg wp)")
    parser.add_argument("--greeting", type=str, default="glhf! \U0001f916", help="Message to send at game start")
    parser.add_argument("--gg-message", type=str, default="gg wp!", help="Message to send at game end")
    parser.add_argument("--move-overhead", type=int, default=100, help="Move overhead in ms — safety buffer for network latency (default: 100)")
    parser.add_argument("--max-concurrent", type=int, default=1, help="Max simultaneous games (default: 1)")
    parser.add_argument("--accept-rapid", action="store_true", help="Accept rapid games up to 15 minutes (default: blitz/bullet only)")
    parser.add_argument("--include-chess960", action="store_true", default=False, help="Include Chess960 challenges in auto-challenger (randomly alternates with standard)")
    parser.add_argument("--auto-open-game", action="store_true", default=False, help="Open each game in default browser when it starts (off by default)")
    args = parser.parse_args()

    _check_license()

    import live_config
    live_config.init({
        "min_rating": args.min_rating,
        "max_games": args.max_games if args.max_games > 0 else 0,
        "skill_level": args.skill,
        "max_depth": args.depth if args.depth > 0 else 0,
        "speed_multiplier": args.speed,
        "rated_challenges": bool(args.rated),
        "enable_challenger": not args.no_challenger,
        "tc_minutes": args.tc_minutes,
        "tc_increment": args.tc_increment,
        "use_nnue": not args.no_nnue,
        "auto_resign": bool(args.auto_resign),
        "resign_threshold": args.resign_threshold,
        "threads": args.threads or 0,
        "hash_size": args.hash or 0,
        "move_overhead": args.move_overhead,
        "enable_chat": not args.no_chat,
        "greeting": args.greeting,
        "gg_message": args.gg_message,
        "max_concurrent_games": args.max_concurrent,
        "accept_rapid": bool(args.accept_rapid),
        "include_chess960": bool(args.include_chess960),
        "auto_open_game": bool(args.auto_open_game),
    })

    # Import after args parsed so config loads properly
    from config import LICHESS_API_TOKEN
    from bot import LichessBot

    token = args.token or LICHESS_API_TOKEN
    if not token:
        print("ERROR: No Lichess API token provided. Set LICHESS_API_TOKEN in .env or pass --token.")
        sys.exit(1)

    max_games = args.max_games if args.max_games > 0 else None
    max_depth = args.depth if args.depth > 0 else None

    print(f"=== Lichess Bot CLI ===")
    print(f"Rating: {args.min_rating} | Rated: {args.rated} | Challenger: {not args.no_challenger}")
    print(f"Skill: {args.skill} | Depth: {max_depth or 'inf'} | Speed: {args.speed}x")
    print(f"Threads: {args.threads or 'auto'} | Hash: {args.hash or 'auto'} MB | Move Overhead: {args.move_overhead} ms")
    print(f"Max Games: {max_games or 'unlimited'}")
    print(f"=======================")

    # If the GUI sends the "Default" string, use the configured default defined in config.py
    from config import STOCKFISH_PATH, BOOK_PATH
    resolved_engine = args.engine_path
    if not resolved_engine or resolved_engine == "Default Stockfish 18":
        resolved_engine = STOCKFISH_PATH

    bot = LichessBot(
        token,
        min_rating=args.min_rating,
        enable_challenger=not args.no_challenger,
        rated_challenges=args.rated,
        max_games=max_games,
        skill_level=args.skill,
        max_depth=max_depth,
        speed_multiplier=args.speed,
        tc_minutes=args.tc_minutes,
        tc_increment=args.tc_increment,
        engine_path=resolved_engine,
        book_path=args.book_path if args.book_path and args.book_path not in ("None", "Default gm_openings.bin") else BOOK_PATH,
        use_nnue=not args.no_nnue,
        auto_resign=args.auto_resign,
        resign_threshold=args.resign_threshold,
        threads=args.threads,
        hash_size=args.hash,
        move_overhead=args.move_overhead,
        enable_chat=not args.no_chat,
        greeting=args.greeting,
        gg_message=args.gg_message,
        max_concurrent_games=args.max_concurrent,
        accept_rapid=args.accept_rapid,
        include_chess960=args.include_chess960,
        auto_open_game=args.auto_open_game
    )

    try:
        bot.start()
    except KeyboardInterrupt:
        print("Shutting down...")
        bot.stop_event.set()

if __name__ == "__main__":
    main()
