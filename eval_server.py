import json
import threading
from http.server import HTTPServer, BaseHTTPRequestHandler
from urllib.parse import urlparse, parse_qs

# Global store for the latest evaluation data per game
_eval_data = {}
_eval_lock = threading.Lock()

class EvalHTTPRequestHandler(BaseHTTPRequestHandler):
    def log_message(self, format, *args):
        # Suppress standard logging to prevent console spam
        pass

    def do_OPTIONS(self):
        self.send_response(200, "ok")
        self.send_header('Access-Control-Allow-Origin', '*')
        self.send_header('Access-Control-Allow-Methods', 'GET, OPTIONS')
        self.send_header("Access-Control-Allow-Headers", "X-Requested-With")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")
        self.end_headers()

    def do_GET(self):
        parsed_path = urlparse(self.path)
        if parsed_path.path == '/eval':
            query_components = parse_qs(parsed_path.query)
            game_id = query_components.get("game", [None])[0]
            
            self.send_response(200)
            self.send_header('Content-type', 'application/json')
            self.send_header('Access-Control-Allow-Origin', '*') # Allow Lichess scripts to read
            self.end_headers()

            with _eval_lock:
                if game_id and game_id in _eval_data:
                    data = _eval_data[game_id]
                    response = json.dumps(data)
                else:
                    response = json.dumps({"score": None, "depth": None, "status": "waiting"})
            
            self.wfile.write(response.encode('utf-8'))
        else:
            self.send_response(404)
            self.end_headers()

def update_eval(game_id: str, score: float, depth: int):
    """Called by bot.py whenever a new move is calculated."""
    with _eval_lock:
        _eval_data[game_id] = {
            "score": score,
            "depth": depth,
            "status": "calculating" if score is None else "done"
        }

def start_eval_server(port=8282):
    server = HTTPServer(('127.0.0.1', port), EvalHTTPRequestHandler)
    print(f"[Eval Server] Started local evaluation API on http://127.0.0.1:{port}")
    server_thread = threading.Thread(target=server.serve_forever, daemon=True)
    server_thread.start()
    return server
