"""One-time DonationAlerts OAuth2 authorization. Run once to get access token."""
import http.server
import urllib.parse
import webbrowser
import requests
import json
import os
from dotenv import load_dotenv

load_dotenv(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".env"))

CLIENT_ID = os.environ.get("DA_CLIENT_ID", "").strip()
CLIENT_SECRET = os.environ.get("DA_CLIENT_SECRET", "").strip()
if not CLIENT_ID or not CLIENT_SECRET:
    raise SystemExit("ERROR: DA_CLIENT_ID / DA_CLIENT_SECRET are not set in .env")

REDIRECT_URI = "http://localhost:7272/callback"
SCOPE = "oauth-donation-subscribe oauth-donation-index oauth-user-show"

auth_code = None

class Handler(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        global auth_code
        query = urllib.parse.urlparse(self.path).query
        params = urllib.parse.parse_qs(query)
        auth_code = params.get("code", [None])[0]
        self.send_response(200)
        self.send_header("Content-Type", "text/html")
        self.end_headers()
        self.wfile.write(b"<h1>OK! Close this tab.</h1>")

    def log_message(self, *args): pass

auth_url = (
    f"https://www.donationalerts.com/oauth/authorize?"
    f"client_id={CLIENT_ID}&redirect_uri={urllib.parse.quote(REDIRECT_URI)}"
    f"&response_type=code&scope={urllib.parse.quote(SCOPE)}"
)
print("Opening browser...")
webbrowser.open(auth_url)

server = http.server.HTTPServer(("localhost", 7272), Handler)
server.handle_request()

if not auth_code:
    print("ERROR: No auth code received")
    exit(1)

print(f"Auth code: {auth_code[:10]}...")

resp = requests.post(
    "https://www.donationalerts.com/oauth/token",
    data={
        "grant_type": "authorization_code",
        "client_id": CLIENT_ID,
        "client_secret": CLIENT_SECRET,
        "redirect_uri": REDIRECT_URI,
        "code": auth_code,
    },
    timeout=30,
)
resp.raise_for_status()
tokens = resp.json()

print(f"Access token: {tokens['access_token'][:20]}...")
print(f"Refresh token: {tokens.get('refresh_token', 'none')[:20]}...")

token_file = os.path.join(os.path.dirname(os.path.abspath(__file__)), "da_tokens.json")
with open(token_file, "w") as f:
    json.dump(tokens, f, indent=2)
print(f"Saved to {token_file}")
