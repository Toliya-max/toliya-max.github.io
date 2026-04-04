import threading
import time
import requests
import chess
from engine import EngineManager

class PuzzleSolverThread(threading.Thread):
    def __init__(self, interval_seconds=300):
        super().__init__()
        self.interval = interval_seconds
        self.daemon = True
        self.running = True

    def run(self):
        print(f"Puzzle Solver Thread started. Will fetch a puzzle every {self.interval} seconds.")
        while self.running:
            try:
                self.solve_next_puzzle()
            except Exception as e:
                print(f"Puzzle Solver encountered an error: {e}")
            
            time.sleep(self.interval)

    def solve_next_puzzle(self):
        # Fetch the next puzzle from the Lichess API
        url = "https://lichess.org/api/puzzle/next"
        response = requests.get(url)
        if response.status_code != 200:
            print(f"Failed to fetch puzzle. Status Code: {response.status_code}")
            return

        data = response.json()
        puzzle_id = data.get("puzzle", {}).get("id")
        rating = data.get("puzzle", {}).get("rating")
        pgn = data.get("game", {}).get("pgn", "")
        initial_ply = data.get("puzzle", {}).get("initialPly")
        
        if not all([puzzle_id, pgn, initial_ply]):
            print("Incomplete puzzle data received.")
            return

        print(f"Solving puzzle {puzzle_id} (Rating: {rating})")

        # Reconstruct the board up to the point of the puzzle
        board = chess.Board()
        moves = pgn.split(" ")
        # initialPly in Lichess represents the number of half-moves.
        # But wait, pgn string separates moves with spaces, maybe we just push initialPly moves.
        # Let's clean up PGN: it might have move numbers like "1. e4 e5 2. Nf3 Nc6" we only want the SAN/UCI moves.
        # Lichess puzzle daily API PGN is just space separated UCI moves: "e4 b6 Nf3 Bb7 e5 c5..."
        
        valid_moves = []
        for m in moves:
            # PGN might contain move numbers, filter them out if present
            if "." not in m and m:
                valid_moves.append(m)

        for i in range(initial_ply):
            if i < len(valid_moves):
                try:
                    board.push_san(valid_moves[i])
                except Exception as e:
                    print(f"Error pushing move {valid_moves[i]}: {e}")
                    return

        print(f"Puzzle FEN: {board.fen()}")

        eng = EngineManager.get_engine()
        try:
            solution = eng.solve_puzzle(board.fen(), depth=15)
            
            if not solution:
                print(f"No solution found for puzzle {puzzle_id}.")
                return

            print(f"Puzzle {puzzle_id} solution PV (Depth 15):")
            san_moves = []
            for move in solution:
                san_moves.append(board.san(move))
                board.push(move)
                
            print(" -> ".join(san_moves))
        finally:
            eng.quit()

if __name__ == "__main__":
    print("Testing PuzzleSolver directly...")
    solver = PuzzleSolverThread(interval_seconds=10)
    solver.solve_next_puzzle()
