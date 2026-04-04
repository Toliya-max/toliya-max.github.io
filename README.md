<div align="center">
  <h1>♞ Lichess Bot Controller</h1>
  <p><b>WPF desktop client for automated Lichess BOT gameplay. Plays at 3000+ Elo. Built with C# (.NET 9) and Python.</b></p>
</div>

## Architecture

Two-process design: the C# GUI launches the Python backend as a subprocess and reads its stdout in real time.

| Component | Description |
|-----------|-------------|
| `LichessBotGUI/` | WPF frontend — reads/writes `settings.json` on open/close, spawns `cli.py` |
| `LichessBotSetup/` | One-click installer |
| `cli.py` | CLI entry point, parses all GUI arguments, creates `LichessBot` |
| `bot.py` | Core bot logic — event streaming, game handling, challenger, chat |
| `engine.py` | Stockfish wrapper (`ChessEngine`, `EngineManager`) with polyglot book support |
| `config.py` | Reads `.env`, resolves paths, auto-detects optimal CPU threads and RAM hash |
| `eval_server.py` | Local HTTP server on `127.0.0.1:8282` — serves live engine evaluation as JSON |


## Features

- **3000+ Elo** — Stockfish 18 at full strength with NNUE, no skill cap, hardware-tuned
- **Stockfish 18** bundled (`stockfish18/stockfish-windows-x86-64-avx2.exe`)
- **GM Opening Book** — `gm_openings.bin` loaded by default (Polyglot format, weighted move selection)
- **Auto hardware tuning** — threads = `CPU_count - 1`, hash = `33% of RAM` (min 256 MB, max 32768 MB)
- **Three engine profiles**: `Stockfish_Max`, `Stockfish_Tactical`, `Stockfish_Fast` (skill-capped)
- **NNUE** enabled by default; can be toggled off via `--no-nnue`
- **Time management** — native Stockfish clock manager with emergency hard caps for <2 s
- **Auto-Resign** — resigns when eval drops below a configurable threshold (default: −5.0)
- **Auto-Challenger** — challenges high-rated online bots with configurable time control and rating floor
- **Chess960** support — auto-detects variant from `gameFull` event
- **Chat** — sends configurable greeting and GG message; can be disabled
- **Accepts**: standard, chess960, fromPosition; declines all other variants and non-bullet/blitz

## Setup

### Prerequisites
- Python 3.10+
- .NET 9.0 SDK (build from source only)

### Install Python dependencies
```bash
python -m venv venv
venv\Scripts\activate
pip install -r requirements.txt
```

### Configure API token
Create a `.env` file in the project root:
```
LICHESS_API_TOKEN=lip_your_token_here
```
The token needs **"Bot: Play games"** permission. Your Lichess account must be upgraded to a BOT account (irreversible).

### Run without GUI
```bash
python cli.py --token lip_xxx --min-rating 2200 --rated --tc-minutes 1 --tc-increment 0
```

### Build the GUI
```bash
cd LichessBotGUI
dotnet build -c Release
```

## CLI Reference

| Argument | Default | Description |
|---|---|---|
| `--token` | `.env` | Lichess API token |
| `--min-rating` | `1900` | Minimum opponent blitz rating for challenger |
| `--max-games` | `0` (unlimited) | Stop after N games |
| `--skill` | `20` | Stockfish Skill Level 0–20 (20 = full strength) |
| `--depth` | `0` (unlimited) | Hard depth cap |
| `--speed` | `1.0` | Clock speed multiplier (>1 = play faster) |
| `--rated` | off | Send rated challenges |
| `--no-challenger` | off | Disable auto-challenger |
| `--tc-minutes` | `2.0` | Base time in minutes |
| `--tc-increment` | `1` | Increment in seconds |
| `--engine-path` | bundled SF18 | Custom UCI engine path |
| `--book-path` | `gm_openings.bin` | Custom Polyglot book path |
| `--no-nnue` | off | Disable NNUE evaluation |
| `--auto-resign` | off | Enable auto-resign |
| `--resign-threshold` | `-5.0` | Pawn eval threshold for resign |
| `--threads` | auto | Override CPU thread count |
| `--hash` | auto | Override hash size in MB |
| `--move-overhead` | `100` | Network latency buffer in ms |
| `--no-chat` | off | Disable chat messages |
| `--greeting` | `glhf! 🤖` | Message sent at game start |
| `--gg-message` | `gg wp!` | Message sent at game end |

## Disclaimer

This software operates at superhuman strength. Lichess prohibits computer assistance on human accounts. **You must upgrade your account to a BOT account before use.** Violations result in a permanent IP ban. Use at your own risk.
