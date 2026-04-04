import chess
import chess.engine
import chess.polyglot
import random
import os
from config import STOCKFISH_PATH, ENGINES
import time

class ChessEngine:
    def __init__(self, path=STOCKFISH_PATH, options=None, book_path=None):
        self.path = path
        self.engine = chess.engine.SimpleEngine.popen_uci(path)
        self.book_path = book_path
        # Unleashed = no Skill Level / UCI_LimitStrength handicap active
        self.is_unleashed = True

        if options:
            use_nnue = options.get("Use NNUE", True)
            # Strip keys we handle ourselves before forwarding to the engine
            options_for_engine = {k: v for k, v in options.items() if k not in ["Use NNUE"]}

            # Skill Level 20 means "no limit at all" — remove it so Stockfish
            # runs at its natural full strength (equivalent to ~3500 Elo).
            if options_for_engine.get("Skill Level") == 20:
                del options_for_engine["Skill Level"]

            # If any explicit skill cap or strength limit is present, mark as limited.
            if options_for_engine.get("UCI_LimitStrength") or "Skill Level" in options_for_engine:
                self.is_unleashed = False

            # Always explicitly disable strength limiter in the max-power path.
            if self.is_unleashed and "UCI_LimitStrength" not in options_for_engine:
                options_for_engine["UCI_LimitStrength"] = False

            self.engine.configure(options_for_engine)

            # Diagnostics
            threads = options.get("Threads", "?")
            hash_mem = options.get("Hash", "?")
            mode_tag = "[UNLEASHED MODE]" if self.is_unleashed else "[SKILL MODE]"
            skill_info = f"Skill Level {options.get('Skill Level', '—')}" if not self.is_unleashed else "MAXIMUM STRENGTH (3500+ Elo)"
            print(f"\n{mode_tag} {skill_info}")
            nnue_tag = "[NNUE ACTIVATED] Neural Network Evaluation is running!" if use_nnue else "[CLASSIC MODE] Neural Network Evaluation is DISABLED!"
            print(f"[HARDWARE] {threads} CPU Threads | {hash_mem} MB Hash | NNUE: {'ON' if use_nnue else 'OFF'}")
            if self.book_path:
                print(f"[BOOK] Opening book: {self.book_path}\n")
            else:
                print()
        else:
            # No options provided — still run at full strength (no Skill Level cap).
            self.engine.configure({"UCI_LimitStrength": False})
            print("\n[UNLEASHED MODE] Running at MAXIMUM STRENGTH (no options override).\n")

    def get_best_move(self, board: chess.Board, time_limit: float = 0.1, wtime=None, btime=None, winc=None, binc=None, max_depth=None, speed_multiplier=1.0, return_score=False):
        """
        Return the engine's best move, optionally with score and depth.

        Time management strategy (in priority order):
        1. Emergency hard cap: when remaining time is critically low.
        2. Native Stockfish clock management: pass wtime/btime/winc/binc so
           Stockfish allocates time optimally based on game phase and complexity.
        3. Fixed-time fallback: used only when no clock info is available.
        """
        # --- Opening book ---
        if self.book_path and os.path.exists(self.book_path):
            try:
                with chess.polyglot.MemoryMappedReader(self.book_path) as reader:
                    entry = reader.weighted_choice(board)  # weighted_choice picks better moves more often
                    if entry:
                        print(f"[BOOK] Book move: {entry.move}")
                        if return_score:
                            return entry.move, 0.0, 0
                        return entry.move
            except Exception:
                pass

        if wtime is not None and btime is not None:
            # Scale the clock by the speed multiplier before passing to Stockfish.
            # speed_multiplier > 1 → play faster (perceived time is shorter)
            wtime_s = wtime / speed_multiplier if speed_multiplier != 1.0 else wtime
            btime_s = btime / speed_multiplier if speed_multiplier != 1.0 else btime
            winc_s = (winc or 0.0)
            binc_s = (binc or 0.0)

            my_time = wtime_s if board.turn == chess.WHITE else btime_s

            if my_time < 0.3:
                # Under 300ms — absolute emergency, pre-move speed.
                limit_kwargs: dict = {"time": 0.02}
            elif my_time < 1.0:
                # Under 1 second — emergency, very fast move.
                limit_kwargs = {"time": 0.05}
            elif my_time < 2.0:
                # Under 2 seconds — tight time pressure.
                limit_kwargs = {"time": 0.1}
            else:
                # Normal play: let Stockfish's own time manager decide.
                # This is the strongest path — Stockfish knows best how
                # to allocate time based on game phase and position complexity.
                limit_kwargs = {
                    "white_clock": wtime_s,
                    "black_clock": btime_s,
                    "white_inc": winc_s,
                    "black_inc": binc_s,
                }
        else:
            # No clock info — use a generous fixed-time search.
            limit_kwargs = {"time": max(time_limit, 10.0) if self.is_unleashed else time_limit * speed_multiplier}

        # Optional hard depth ceiling (0 / None = no limit, let time manager rule).
        if max_depth:
            limit_kwargs["depth"] = max_depth

        limit = chess.engine.Limit(**limit_kwargs)

        if return_score:
            info = self.engine.analyse(board, limit)
            best_move = info.get("pv", [None])[0]

            score_obj = info.get("score")
            score_val = None
            if score_obj:
                pov_score = score_obj.pov(board.turn)
                if pov_score.is_mate():
                    mate_dist = pov_score.mate()
                    score_val = 1000.0 if mate_dist > 0 else -1000.0
                else:
                    score_val = pov_score.score() / 100.0

            depth = info.get("depth", 0)
            return best_move, score_val, depth
        else:
            result = self.engine.play(board, limit)
            return result.move
        
    def solve_puzzle(self, fen: str, depth: int = 20) -> list[chess.Move]:
        """
        Solves a puzzle from a given FEN string by calculating the best line up to `depth`.
        """
        board = chess.Board(fen)
        limit = chess.engine.Limit(depth=depth)
        info = self.engine.analyse(board, limit)
        return info.get("pv", [])

    def quit(self):
        self.engine.quit()

class EngineManager:
    """Manages different engine profiles defined in config.py."""

    @staticmethod
    def get_engine(
        profile_name: str = None,
        skill_level: int = None,
        engine_path: str = None,
        book_path: str = None,
        use_nnue: bool = True,
        threads: int = None,
        hash_size: int = None,
        move_overhead: int = 100,
    ) -> ChessEngine:
        """Return a fully configured ChessEngine, tuned for maximum strength."""
        from config import OPT_THREADS, OPT_HASH

        if engine_path and os.path.exists(engine_path):
            print(f"EngineManager: Using custom engine path '{engine_path}'")
            path_to_use = engine_path
            # Start from system-optimal defaults for a custom engine path.
            options = {
                "Threads": OPT_THREADS,
                "Hash": OPT_HASH,
                "UCI_LimitStrength": False,
                "Move Overhead": move_overhead,
            }
        else:
            if not profile_name or profile_name not in ENGINES:
                # Always prefer the strongest profile when no profile is specified.
                profile_name = "Stockfish_Max"
                print(f"EngineManager: Defaulting to max-strength profile '{profile_name}'")
            else:
                print(f"EngineManager: Using profile '{profile_name}'")

            config = ENGINES[profile_name]
            options = config.get("options", {}).copy()
            path_to_use = config["path"]

        # Apply skill-level override from GUI.
        if skill_level is not None:
            if skill_level == 20:
                # Absolute maximum strength: remove every handicap option.
                options.pop("Skill Level", None)
                options["UCI_LimitStrength"] = False
                # Threads: use system optimal (or whatever was already in the profile).
                if "Threads" not in options or options["Threads"] < OPT_THREADS:
                    options["Threads"] = OPT_THREADS
                # Hash: use the larger of the system-computed OPT_HASH and 4096 MB.
                options["Hash"] = max(OPT_HASH, 4096)
                options["Move Overhead"] = move_overhead
            else:
                options["Skill Level"] = skill_level
                options["UCI_LimitStrength"] = True
                options["Move Overhead"] = move_overhead

        # Explicit hardware overrides from GUI take absolute precedence.
        if threads is not None and threads > 0:
            options["Threads"] = threads
        if hash_size is not None and hash_size > 0:
            options["Hash"] = hash_size

        options["Use NNUE"] = use_nnue

        # Initialize the engine process.
        engine = ChessEngine(path=path_to_use, options=options, book_path=book_path)

        # NNUE: if disabled, clear the eval file so Stockfish falls back to HCE.
        if not use_nnue:
            try:
                engine.engine.configure({"EvalFile": ""})
            except Exception:
                print("WARNING: This Stockfish build does not support disabling NNUE via EvalFile.")

        return engine

if __name__ == "__main__":
    eng = EngineManager.get_engine()
    board = chess.Board()
    print(f"Engine loaded successfully: {eng.path}. Testing initial position move:")
    move = eng.get_best_move(board, time_limit=1.0)
    print(f"Best move: {move}")
    
    fen_puzzle = "r1bqk2r/pppp1ppp/2n2n2/2b1p3/2B1P3/2N2N2/PPPP1PPP/R1BQK2R w KQkq - 6 5"
    print(f"\nSolving puzzle FEN: {fen_puzzle}")
    moves = eng.solve_puzzle(fen_puzzle, depth=15)
    print(f"Solution principal variation: {moves}")
    
    eng.quit()
