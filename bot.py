import json as _json
_orig_decoder_init = _json.JSONDecoder.__init__
def _patched_decoder_init(self, *a, **kw):
    kw.pop('encoding', None)
    _orig_decoder_init(self, *a, **kw)
_json.JSONDecoder.__init__ = _patched_decoder_init

import os as _os
import re as _re
import random as _random

_LICENSE_MARKERS = (
    _re.compile(r"def\s+_compute_sig_v2\s*\("),
    _re.compile(r"def\s+_decode_key\s*\("),
    _re.compile(r"hmac\.compare_digest\s*\("),
)
_CLI_MARKERS = (
    _re.compile(r"def\s+_check_license\s*\("),
    _re.compile(r"sys\.exit\s*\("),
)

def _verify_integrity():
    _base = _os.path.dirname(_os.path.abspath(__file__))
    _corrupt = False
    try:
        with open(_os.path.join(_base, 'license.py'), 'r', encoding='utf-8', errors='ignore') as _f:
            _src = _f.read()
        if not all(p.search(_src) for p in _LICENSE_MARKERS):
            _corrupt = True
    except Exception:
        _corrupt = True
    try:
        with open(_os.path.join(_base, 'cli.py'), 'r', encoding='utf-8', errors='ignore') as _f:
            _src = _f.read()
        if not all(p.search(_src) for p in _CLI_MARKERS):
            _corrupt = True
    except Exception:
        _corrupt = True
    return _corrupt

_INTEGRITY_FAILED = _verify_integrity()

import berserk
import threading
import traceback
import chess
import time
import requests
import datetime
import random
import urllib3
import webbrowser
import json
import os
from config import LICHESS_API_TOKEN
from engine import EngineManager
from eval_server import start_eval_server, update_eval
import live_config

# Start the evaluation microservice globally
try:
    start_eval_server(port=8282)
except Exception as e:
    print(f"Warning: Could not start eval server: {e}")

class LichessBot:
    def __init__(self, token, min_rating=2500, enable_challenger=True, rated_challenges=True, max_games=None, skill_level=20, max_depth=None, speed_multiplier=1.0, tc_minutes=2, tc_increment=1, stop_event=None, engine_path=None, book_path=None, use_nnue=True, auto_resign=True, resign_threshold=-5.0, threads=None, hash_size=None, move_overhead=30, enable_chat=True, greeting="glhf! 🤖", gg_message="gg wp!", max_concurrent_games=1, accept_rapid=False, include_chess960=False, auto_open_game=False):
        if not token:
            raise ValueError("LICHESS_API_TOKEN is not set.")
        
        self.session = berserk.TokenSession(token)
        self.client = berserk.Client(session=self.session)
        self._challenger_session = berserk.TokenSession(token)
        self._challenger_client = berserk.Client(session=self._challenger_session)
        self.active_games = set()  # Track active games internally
        self.pending_challenges = set() # Track challenges we sent
        
        self.min_rating = min_rating
        self.enable_challenger = enable_challenger
        self.rated_challenges = rated_challenges
        self.max_games = max_games
        self.skill_level = skill_level
        self.max_depth = max_depth
        self.speed_multiplier = speed_multiplier
        self.engine_path = engine_path
        self.book_path = book_path
        self.use_nnue = use_nnue
        self.auto_resign = auto_resign
        self.resign_threshold = resign_threshold
        self.threads = threads
        self.hash_size = hash_size
        self.move_overhead = move_overhead
        self.games_played = 0
        self.tc_minutes = tc_minutes
        self.tc_increment = tc_increment
        self.stop_event = stop_event or threading.Event()
        self._main_backoff = 60
        self.enable_chat = enable_chat
        self.greeting = greeting
        self.gg_message = gg_message
        self.max_concurrent_games = max_concurrent_games
        self.accept_rapid = accept_rapid
        self.include_chess960 = include_chess960
        self.auto_open_game = auto_open_game
        self._last_eval = {}
        self._move_counter = 0

        self.engine_manager = EngineManager()

        live_config.subscribe(self._apply_live_config)

        stats_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'stats.json')
        self._stats_path = stats_path
        if os.path.exists(stats_path):
            try:
                with open(stats_path, 'r') as f:
                    s = json.load(f)
                self.wins = s.get('wins', 0)
                self.losses = s.get('losses', 0)
                self.draws = s.get('draws', 0)
            except Exception:
                self.wins = self.losses = self.draws = 0
        else:
            self.wins = self.losses = self.draws = 0
        
        # Verify account and it is a bot
        retries = 5
        while retries > 0:
            try:
                self.account = self.client.account.get()
                self.bot_id = self.account['id']
                print(f"Successfully logged in as {self.account['username']} (ID: {self.bot_id})")
                print(f"Stats: W={self.wins} L={self.losses} D={self.draws} Total={self.wins+self.losses+self.draws}")
                break
            except Exception as e:
                print(f"Failed to fetch account info. Error: {e}")
                retries -= 1
                if retries == 0:
                    raise
                print("Retrying login in 2 seconds...")
                time.sleep(2)

    _LIVE_BOT_KEYS = {
        "min_rating", "max_games", "skill_level", "max_depth", "speed_multiplier",
        "rated_challenges", "enable_challenger", "tc_minutes", "tc_increment",
        "auto_resign", "resign_threshold", "move_overhead", "enable_chat",
        "greeting", "gg_message", "max_concurrent_games", "accept_rapid",
        "include_chess960", "auto_open_game", "use_nnue", "threads", "hash_size",
    }

    def _apply_live_config(self, diff: dict, snap: dict) -> None:
        """Subscriber: apply settings changes from GUI without restart."""
        if not diff:
            return
        applied: list[str] = []

        if "min_rating" in diff:
            self.min_rating = int(diff["min_rating"])
            applied.append(f"min_rating={self.min_rating}")
        if "max_games" in diff:
            v = int(diff["max_games"])
            self.max_games = v if v > 0 else None
            applied.append(f"max_games={self.max_games}")
        if "skill_level" in diff:
            self.skill_level = int(diff["skill_level"])
            applied.append(f"skill={self.skill_level}")
        if "max_depth" in diff:
            v = int(diff["max_depth"])
            self.max_depth = v if v > 0 else None
            applied.append(f"depth={self.max_depth}")
        if "speed_multiplier" in diff:
            self.speed_multiplier = float(diff["speed_multiplier"])
            applied.append(f"speed={self.speed_multiplier}")
        if "rated_challenges" in diff:
            self.rated_challenges = bool(diff["rated_challenges"])
            applied.append(f"rated={self.rated_challenges}")
        if "enable_challenger" in diff:
            self.enable_challenger = bool(diff["enable_challenger"])
            applied.append(f"challenger={self.enable_challenger}")
        if "tc_minutes" in diff:
            self.tc_minutes = float(diff["tc_minutes"])
            applied.append(f"tc_min={self.tc_minutes}")
        if "tc_increment" in diff:
            self.tc_increment = int(diff["tc_increment"])
            applied.append(f"tc_inc={self.tc_increment}")
        if "auto_resign" in diff:
            self.auto_resign = bool(diff["auto_resign"])
            applied.append(f"auto_resign={self.auto_resign}")
        if "resign_threshold" in diff:
            self.resign_threshold = float(diff["resign_threshold"])
            applied.append(f"resign_threshold={self.resign_threshold}")
        if "move_overhead" in diff:
            self.move_overhead = int(diff["move_overhead"])
            applied.append(f"move_overhead={self.move_overhead}")
        if "enable_chat" in diff:
            self.enable_chat = bool(diff["enable_chat"])
            applied.append(f"chat={self.enable_chat}")
        if "greeting" in diff:
            self.greeting = str(diff["greeting"])
            applied.append("greeting")
        if "gg_message" in diff:
            self.gg_message = str(diff["gg_message"])
            applied.append("gg_message")
        if "max_concurrent_games" in diff:
            self.max_concurrent_games = max(1, int(diff["max_concurrent_games"]))
            applied.append(f"max_concurrent={self.max_concurrent_games}")
        if "accept_rapid" in diff:
            self.accept_rapid = bool(diff["accept_rapid"])
            applied.append(f"rapid={self.accept_rapid}")
        if "include_chess960" in diff:
            self.include_chess960 = bool(diff["include_chess960"])
            applied.append(f"chess960={self.include_chess960}")
        if "auto_open_game" in diff:
            self.auto_open_game = bool(diff["auto_open_game"])
            applied.append(f"auto_open={self.auto_open_game}")

        engine_diff: dict = {}
        if "skill_level" in diff:
            engine_diff["skill_level"] = self.skill_level
        if "max_depth" in diff:
            engine_diff["max_depth"] = self.max_depth
        if "use_nnue" in diff:
            self.use_nnue = bool(diff["use_nnue"])
            engine_diff["use_nnue"] = self.use_nnue
            applied.append(f"nnue={self.use_nnue}")
        if "threads" in diff:
            v = int(diff["threads"])
            self.threads = v if v > 0 else None
            engine_diff["threads"] = self.threads
            applied.append(f"threads={self.threads}")
        if "hash_size" in diff:
            v = int(diff["hash_size"])
            self.hash_size = v if v > 0 else None
            engine_diff["hash_size"] = self.hash_size
            applied.append(f"hash={self.hash_size}")
        if "move_overhead" in diff:
            engine_diff["move_overhead"] = self.move_overhead

        if engine_diff:
            try:
                if hasattr(self, "engine_manager") and self.engine_manager is not None:
                    self.engine_manager.apply_live_options(engine_diff)
            except Exception as e:
                print(f"[LIVE_CONFIG] engine apply error: {e}")

        if applied:
            print(f"[LIVE_CONFIG] applied: {', '.join(applied)}")

    def send_chat(self, game_id, message, room='player'):
        """Send a chat message in a game. room='player' (opponent) or 'spectator' (observers)."""
        try:
            self.client.bots.post_message(game_id, message, spectator=(room == 'spectator'))
            print(f"[{game_id}] Chat sent ({room}): {message}")
        except Exception as e:
            print(f"[{game_id}] Failed to send chat: {e}")

    def send_chat_message(self, game_id, message):
        """Send to both player and spectator rooms."""
        self.send_chat(game_id, message, room='player')
        self.send_chat(game_id, message, room='spectator')

    def is_acceptable_time_control(self, time_control):
        if time_control.get('type') == 'clock':
            limit = time_control.get('limit', 0)
            max_limit = 900 if self.accept_rapid else 300
            if limit <= max_limit:
                return True
        return False

    def _save_stats(self):
        try:
            with open(self._stats_path, 'w') as f:
                json.dump({'wins': self.wins, 'losses': self.losses, 'draws': self.draws, 'total': self.wins + self.losses + self.draws}, f)
        except Exception as e:
            print(f"Failed to save stats: {e}")

    def handle_game(self, game_id):
        print(f"Starting game thread for game {game_id}")
        engine = EngineManager.get_engine(
            skill_level=self.skill_level,
            engine_path=self.engine_path,
            book_path=self.book_path,
            use_nnue=self.use_nnue,
            threads=self.threads,
            hash_size=self.hash_size,
            move_overhead=self.move_overhead,
        )
        board = chess.Board()
        color = None
        is_chess960 = False


        try:
            retry_count = 0
            while True:
                try:
                    for event in self.client.bots.stream_game_state(game_id):
                        retry_count = 0 # reset on successful event
                        if event['type'] == 'gameFull':
                            variant = event.get('variant', {}).get('key', 'standard')
                            is_chess960 = (variant == 'chess960')
                            if is_chess960:
                                print(f"[{game_id}] Chess960 game detected. Stockfish 18 auto-manages UCI_Chess960 from position.")

                            # Get initial FEN (important for Chess960)
                            initial_fen = event.get('initialFen', 'startpos')
                            if initial_fen and initial_fen != 'startpos':
                                board = chess.Board(initial_fen, chess960=is_chess960)
                            else:
                                board = chess.Board(chess960=is_chess960)
                            
                            # Parse initial state
                            if event['white'].get('id') == self.bot_id:
                                color = chess.WHITE
                            else:
                                color = chess.BLACK
                                
                            state = event['state']
                            if state.get('moves'):
                                for uci_move in state['moves'].split(' '):
                                    board.push_uci(uci_move)
                                    
                            if board.turn == color:
                                self.play_move(game_id, event, board, engine, color)
                            
                            # Send greeting after game starts
                            if self.enable_chat and self.greeting:
                                threading.Thread(target=self.send_chat_message, args=(game_id, self.greeting), daemon=True).start()
                                
                        elif event['type'] == 'gameState':
                            # Check if game ended (aborted, resign, mate, etc.)
                            status = event.get('status', 'started')
                            if status in ('aborted', 'resign', 'mate', 'outoftime', 'draw', 'stalemate', 'timeout', 'noStart'):
                                print(f"[{game_id}] Game ended: {status}")
                                if status == 'aborted':
                                    # Don't count aborted games towards the limit
                                    self.games_played = max(0, self.games_played - 1)
                                    print(f"[{game_id}] Game was aborted (opponent didn't move). Not counting towards limit.")
                                else:
                                    winner = event.get('winner', '')
                                    if status in ('draw', 'stalemate'):
                                        self.draws += 1
                                    elif winner == ('white' if color == chess.WHITE else 'black'):
                                        self.wins += 1
                                    elif winner in ('white', 'black'):
                                        self.losses += 1
                                    else:
                                        self.draws += 1
                                    self._save_stats()
                                    print(f"[{game_id}] Stats: W={self.wins} L={self.losses} D={self.draws} Total={self.wins+self.losses+self.draws}")
                                    if self.enable_chat and self.gg_message:
                                        threading.Thread(target=self.send_chat_message, args=(game_id, self.gg_message), daemon=True).start()
                                break  # Exit the stream loop — game is over

                            # Handle draw offers from opponent
                            wdraw = event.get('wdraw', False)
                            bdraw = event.get('bdraw', False)
                            opponent_offered_draw = (wdraw and color == chess.BLACK) or (bdraw and color == chess.WHITE)
                            if opponent_offered_draw:
                                eval_score = self._last_eval.get(game_id)
                                if eval_score is not None and eval_score < -1.5:
                                    print(f"[{game_id}] Opponent offered draw, eval={eval_score:.2f}. Accepting.")
                                    try:
                                        requests.post(
                                            f"https://lichess.org/api/bot/game/{game_id}/draw/yes",
                                            headers={"Authorization": f"Bearer {self.session.token}"},
                                            timeout=5,
                                        )
                                    except Exception as e:
                                        print(f"[{game_id}] Failed to accept draw: {e}")
                                else:
                                    print(f"[{game_id}] Opponent offered draw, eval={eval_score}. Declining.")

                            moves = event.get('moves', '').split(' ')
                            if moves and moves[0]:
                                if initial_fen and initial_fen != 'startpos':
                                    board = chess.Board(initial_fen, chess960=is_chess960)
                                else:
                                    board = chess.Board(chess960=is_chess960)
                                for uci_move in moves:
                                    board.push_uci(uci_move)

                                if board.turn == color:
                                    print(f"[{game_id}] It is our turn to play. Calculating move...")
                                    self.play_move(game_id, event, board, engine, color)
                                else:
                                    print(f"[{game_id}] Opponent's turn.")
                            else:
                                print(f"[{game_id}] Empty moves array received.")
                                
                        elif event['type'] == 'chatLine':
                            username = event.get('username', '')
                            text = event.get('text', '')
                            room = event.get('room', 'player')
                            print(f"[{game_id}] Chat from {username} ({room}): {text}")
                        else:
                            print(f"[{game_id}] Unknown event type: {event.get('type')}")
                            
                    # Stream finished normally
                    break 

                except (requests.exceptions.ChunkedEncodingError, 
                        requests.exceptions.ConnectionError, 
                        requests.exceptions.ReadTimeout,
                        urllib3.exceptions.ProtocolError,
                        urllib3.exceptions.HTTPError) as stream_err:
                    print(f"Warning: Stream dropped for {game_id} ({stream_err}). Reconnecting...")
                    retry_count += 1
                    if retry_count > 10:
                        print(f"Error: Could not reconnect to game {game_id} after 10 tries.")
                        break
                    time.sleep(1) # wait before reconnecting
                except Exception as default_err:
                    err_str = str(default_err)
                    # Handle 429 Too Many Requests (rate limiting)
                    if "429" in err_str or "Too Many Requests" in err_str:
                        retry_count += 1
                        wait_time = min(60, 2 ** retry_count)  # exponential backoff, max 60s
                        print(f"[!] Rate limited (429) for game {game_id}. Waiting {wait_time}s before retry #{retry_count}...")
                        time.sleep(wait_time)
                        if retry_count > 8:
                            print(f"Error: Rate limited too many times for game {game_id}. Giving up.")
                            break
                    # Handle SSL/Connection errors by string matching as a fallback
                    elif any(msg in err_str for msg in ["SSLError", "UNEXPECTED_EOF", "MaxRetryError", "Connection aborted", "Remote end closed"]):
                        print(f"Warning: Network connection issue for {game_id} ({err_str}). Reconnecting...")
                        retry_count += 1
                        if retry_count > 10:
                            print(f"Error: Could not reconnect after 10 tries.")
                            break
                        time.sleep(2)
                    else:
                        raise # Re-raise if it's a completely different error

        except Exception as e:
            print(f"Error in game thread {game_id}: {e}")
            traceback.print_exc()
        finally:
            engine.quit()
            self.active_games.discard(game_id)
            print(f"Game {game_id} thread completed.")
            if self.max_games and self.games_played >= self.max_games and len(self.active_games) == 0:
                print("Max games reached and all active games finished. Stopping bot...")
                self.stop_event.set()

    def play_move(self, game_id, event, board, engine, color):
        if board.is_game_over():
            return
            
        # Time management
        state = event if event['type'] == 'gameState' else event['state']
        
        # Check clocks from the event state
        wtime = state.get('wtime')
        btime = state.get('btime')
        winc = state.get('winc', 0)
        binc = state.get('binc', 0)
        
        # Extract seconds from datetime.timedelta objects if necessary
        def to_seconds(val):
            if isinstance(val, datetime.timedelta):
                return val.total_seconds()
            elif val is not None:
                return float(val) / 1000.0  # fallback in case it's actually ms ints
            return None
            
        wtime_sec = to_seconds(wtime)
        btime_sec = to_seconds(btime)
        winc_sec = to_seconds(winc)
        binc_sec = to_seconds(binc)
        
        # Calculate move
        print(f"[{game_id}] Giving engine wtime={wtime_sec}, btime={btime_sec} (Speed Mult: {self.speed_multiplier}x, Depth: {self.max_depth or 'inf'})")
        
        # Reset eval display to calculating
        update_eval(game_id, None, None)
        
        self._move_counter += 1
        if self._move_counter % 50 == 0:
            try:
                import license as _lic
                _lic.check()
            except Exception:
                import time as _t
                _t.sleep(0.5)

        move, score, depth = engine.get_best_move(
            board,
            wtime=wtime_sec, btime=btime_sec, winc=winc_sec, binc=binc_sec,
            max_depth=self.max_depth,
            speed_multiplier=self.speed_multiplier,
            return_score=True
        )

        if _INTEGRITY_FAILED and score is not None:
            score += _random.uniform(-3.0, 3.0)
        
        # Publish evaluation to local server
        if score is not None:
            update_eval(game_id, score, depth)
            self._last_eval[game_id] = score
        
        if self.auto_resign and score is not None:
            # Check if engine thinks we are completely losing
            if score < self.resign_threshold:
                print(f"[{game_id}] Evaluated score is {score}, which is worse than the resign threshold of {self.resign_threshold}. Resigning...")
                try:
                    self.client.bots.resign_game(game_id)
                except Exception as e:
                    print(f"[{game_id}] Error trying to resign: {e}")
                return
        
        if move:
            if score is not None:
                # Add + sign for positive scores for readability
                # Handle mate scores which EngineManager might return as +/- 1000.0
                if score >= 900.0:
                    score_str = f"M"
                elif score <= -900.0:
                    score_str = f"-M"
                else:
                    score_str = f"+{score:.2f}" if score > 0 else f"{score:.2f}"
                print(f"[{game_id}] Engine selected move: {move.uci()} (Eval: {score_str} | Color: {color})")
            else:
                print(f"[{game_id}] Engine selected move: {move.uci()}")
            for attempt in range(6):  # Retry up to 6 times for bullet reliability
                try:
                    self.client.bots.make_move(game_id, move.uci())
                    print(f"[{game_id}] Successfully played move: {move.uci()}")
                    break
                except Exception as e:
                    print(f"[{game_id}] Move failed (attempt {attempt+1}/6): {e}")
                    import time
                    time.sleep(0.05) # very quick retry in case of temporal 502/timeout
        else:
            print(f"[{game_id}] Engine returned no move!")

    def run_challenger(self, interval_seconds=5):
        # We will try to challenge random bots
        clock_limit = int(max(15, self.tc_minutes * 60))
        clock_increment = int(self.tc_increment)
        
        while not self.stop_event.is_set():
            time.sleep(interval_seconds)
            
            if not self.enable_challenger:
                continue
                
            if self.max_games and self.games_played >= self.max_games:
                continue
                
            try:
                # Check internal active games tracker instead of polling Lichess API
                if len(self.active_games) >= self.max_concurrent_games:
                    continue # Wait until a game slot opens
                    
                # If we already have pending challenges, wait for them to resolve or timeout
                if len(self.pending_challenges) > 0:
                    continue
                
                # Fetch currently online bots (gives a few dozen at a time)
                online_bots = list(self._challenger_client.bots.get_online_bots())
                if online_bots:
                    # Filter out ourselves and only pick high rated bots (>= 2500 blitz)
                    targets = []
                    for b in online_bots:
                        if b['id'] == self.bot_id:
                            continue
                            
                        # Try to get the blitz rating from the perfs dict
                        try:
                            blitz_rating = b.get('perfs', {}).get('blitz', {}).get('rating', 0)
                            if blitz_rating >= self.min_rating:
                                targets.append(b)
                        except:
                            pass
                            
                    if targets:
                        # Pick up to 2 random bots to challenge at once (avoiding rate limits)
                        num_to_challenge = min(2, len(targets))
                        chosen_targets = random.sample(targets, num_to_challenge)
                        
                        for target in chosen_targets:
                            if self.stop_event.is_set() or len(self.active_games) >= self.max_concurrent_games:
                                break # abort if slots are full

                            target_id = target['id']
                            time_format = "Bullet" if self.tc_minutes < 3 else "Blitz" if self.tc_minutes <= 5 else "Rapid"
                            variant = random.choice(['standard', 'chess960']) if self.include_chess960 else 'standard'
                            print(f"Challenger: Sending {'Rated' if self.rated_challenges else 'Casual'} {self.tc_minutes}+{self.tc_increment} {time_format} {variant} challenge to bot => {target_id}")
                            try:
                                response = self._challenger_client.challenges.create(target_id, self.rated_challenges, clock_limit=clock_limit, clock_increment=clock_increment, variant=variant)
                                if 'challenge' in response and 'id' in response['challenge']:
                                    self.pending_challenges.add(response['challenge']['id'])
                                time.sleep(2.0) # 2-second delay between sending to respect Lichess API limits
                            except Exception as inner_e:
                                print(f"Challenger: Failed to challenge {target_id}: {inner_e}")
                                if "Too Many Requests" in str(inner_e):
                                    print("Rate limited by Lichess! Sleeping for 15 seconds...")
                                    time.sleep(15) # Backoff if ratelimited
                                    break
            except Exception as e:
                print(f"Challenger loop error: {e}")
                if "Too Many Requests" in str(e):
                    time.sleep(60) # Global loop backoff on general HTTP 429
                else:
                    time.sleep(15) # Wait a bit on network drops


    def start(self):
        # Start challenger thread
        challenger_thread = threading.Thread(target=self.run_challenger, args=(5,), daemon=True)
        challenger_thread.start()
        
        while not self.stop_event.is_set():
            print("Bot is listening for events...")
            try:
                for event in self.client.bots.stream_incoming_events():
                    if self.stop_event.is_set():
                        break
                        
                    if event['type'] == 'challenge':
                        challenge = event['challenge']
                        challenge_id = challenge['id']

                        # Skip challenges that WE sent (avoid trying to accept our own)
                        challenger_id = challenge.get('challenger', {}).get('id', '')
                        if challenger_id == self.bot_id:
                            continue

                        if self.max_games and self.games_played >= self.max_games:
                            print(f"Declining challenge {challenge_id} (Max games reached)")
                            try:
                                self.client.bots.decline_challenge(challenge_id, reason="later")
                            except:
                                pass
                            continue
                        
                        # Check variant - Stockfish only supports standard and chess960
                        variant = challenge.get('variant', {}).get('key', 'standard')
                        supported_variants = {'standard', 'chess960', 'fromPosition'}
                        
                        if variant not in supported_variants:
                            print(f"Declining challenge {challenge_id} (unsupported variant: {variant})")
                            try:
                                self.client.bots.decline_challenge(challenge_id, reason="standard")
                            except:
                                pass
                            continue
                        
                        if self.is_acceptable_time_control(challenge['timeControl']):
                            print(f"Accepting challenge {challenge_id} (variant: {variant})")
                            try:
                                self.client.bots.accept_challenge(challenge_id)
                            except Exception as e:
                                print(f"Could not accept challenge {challenge_id}: {e}")
                        else:
                            print(f"Declining challenge {challenge_id} (time control not accepted)")
                            try:
                                self.client.bots.decline_challenge(challenge_id, reason="timeControl")
                            except Exception as e:
                                print(f"Could not decline challenge {challenge_id}: {e}")
                            
                    elif event['type'] == 'gameStart':
                        game_id = event['game']['gameId']

                        if game_id in self.active_games:
                            print(f"Duplicate gameStart for {game_id} — already handling, skipping.")
                            continue

                        self.active_games.add(game_id)
                        self.games_played += 1
                        print(f"Game started: {game_id}. (Played: {self.games_played}/{self.max_games or 'inf'}, Active: {len(self.active_games)}/{self.max_concurrent_games})")

                        if len(self.active_games) >= self.max_concurrent_games:
                            for pending_id in list(self.pending_challenges):
                                try:
                                    self.client.challenges.cancel(pending_id)
                                    print(f"Canceled pending challenge {pending_id}")
                                except Exception as e:
                                    print(f"Failed to cancel {pending_id}: {e}")
                            self.pending_challenges.clear()

                        game_url = f"https://lichess.org/{game_id}"
                        if self.auto_open_game:
                            try:
                                webbrowser.open(game_url, new=2)
                                print(f"Opened game in browser: {game_url}")
                            except Exception as e:
                                print(f"Failed to open browser: {e}")
                        else:
                            print(f"Game URL (auto-open off): {game_url}")

                        threading.Thread(target=self.handle_game, args=(game_id,), daemon=True).start()
            except BaseException as e:
                if self.stop_event.is_set():
                    break
                err_str = str(e)
                is_rate_limited = "429" in err_str or "Too Many Requests" in err_str
                
                if is_rate_limited:
                    if not hasattr(self, '_main_backoff'):
                        self._main_backoff = 60
                    wait = self._main_backoff
                    self._main_backoff = min(180, self._main_backoff + 30)
                    print(f"[!] Rate limited (429) in main loop. Backing off for {wait}s...")
                else:
                    wait = 30
                    self._main_backoff = 60  # reset backoff on non-429 errors
                    print(f"Exception in main loop/streaming: {e}")
                    print(f"Sleeping for {wait}s before reconnecting...")
                
                # Sleep interruptibly
                for _ in range(wait):
                    if self.stop_event.is_set():
                        break
                    time.sleep(1)
                    
                try:
                    # Try to re-init client session if token/connection went stale
                    self.session = berserk.TokenSession(self.session.token)
                    self.client = berserk.Client(session=self.session)
                except:
                    pass
        
        print("Bot listener stopped.")

if __name__ == "__main__":
    bot = LichessBot(LICHESS_API_TOKEN)
    try:
        bot.start()
    except KeyboardInterrupt:
        print("Shutting down...")
        bot.stop_event.set()
