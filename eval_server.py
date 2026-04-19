import json
import threading
from http.server import HTTPServer, BaseHTTPRequestHandler
from urllib.parse import urlparse, parse_qs

# Global store for the latest evaluation data per game
_eval_data = {}
_eval_lock = threading.Lock()

_ALLOWED_ORIGINS = frozenset({
    "https://lichess.org",
    "http://localhost",
    "http://127.0.0.1",
})

def _pick_origin(request_origin: str) -> str | None:
    if not request_origin:
        return None
    for allowed in _ALLOWED_ORIGINS:
        if request_origin == allowed or request_origin.startswith(allowed + ":"):
            return request_origin
    return None

class EvalHTTPRequestHandler(BaseHTTPRequestHandler):
    def log_message(self, format, *args):
        pass

    def _write_cors(self, origin_header: str | None):
        allowed = _pick_origin(origin_header or "")
        if allowed:
            self.send_header("Access-Control-Allow-Origin", allowed)
            self.send_header("Vary", "Origin")

    def do_OPTIONS(self):
        self.send_response(204, "ok")
        self._write_cors(self.headers.get("Origin"))
        self.send_header("Access-Control-Allow-Methods", "GET, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type, X-Requested-With")
        self.send_header("Access-Control-Max-Age", "600")
        self.end_headers()

    def do_GET(self):
        parsed_path = urlparse(self.path)
        if parsed_path.path == '/eval':
            query_components = parse_qs(parsed_path.query)
            game_id = query_components.get("game", [None])[0]

            self.send_response(200)
            self.send_header('Content-type', 'application/json')
            self._write_cors(self.headers.get("Origin"))
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
