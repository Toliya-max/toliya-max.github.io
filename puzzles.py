import chess
from engine import EngineManager
import sys

def solve_fen(fen: str, depth: int = 15):
    print(f"Solving position: {fen}")
    engine = EngineManager.get_engine()
    
    try:
        moves = engine.solve_puzzle(fen, depth=depth)
        
        if not moves:
            print("No moves found.")
            return

        print(f"Engine found PV (Depth {depth}):")
        
        # Format the moves in standard algebraic notation (SAN) for easier reading
        board = chess.Board(fen)
        san_moves = []
        for move in moves:
            san_moves.append(board.san(move))
            board.push(move)
            
        print(" -> ".join(san_moves))
        
    finally:
        engine.quit()

if __name__ == "__main__":
    if len(sys.argv) > 1:
        fen = sys.argv[1]
    else:
        # Example Lichess puzzle FEN
        fen = "r1bqk2r/pppp1ppp/2n2n2/2b1p3/2B1P3/2N2N2/PPPP1PPP/R1BQK2R w KQkq - 6 5"
        
    solve_fen(fen)
