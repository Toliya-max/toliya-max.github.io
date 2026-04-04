<div align="center">
  <h1>♞ Lichess Bot Controller V2</h1>
  <p><b>Advanced WPF Desktop Client for automated Lichess gameplay. Built with C# and Python.</b></p>
</div>

## ✨ Features
- **Grandmaster Level Play (4000+ Elo):** Comes pre-configured with Stockfish Dev and a massive 1.4 million move Grandmaster opening book to guarantee flawless openings and lightning-fast midgame calculation.
- **Premium Dark UI:** Beautiful, modern Windows Presentation Foundation (WPF) interface.
- **Auto-Challenger:** Automatically sends challenges to high-rated online bots within your specified parameters.
- **Rapid Time Controls (Time Presets):** One-click settings for 1+0 Bullet, 3+0 Blitz, and 10+0 Rapid. Capable of playing sub-second Hyperbullet.
- **Auto-Resign Engine:** Built-in threshold detector that automatically resigns the game if the eval drops below `-5.0` to save time.
- **Custom Engines:** Load any UCI compatible `.exe` engine or `.bin` opening book dynamically through the UI.

## 🚀 Installation
We provide a one-click automated installer that sets up the Python backend, all pip dependencies, and downloads the Stockfish engine automatically.

1. Download the latest `LichessBotGUI.exe` or `Setup_LichessBot.exe` from the Releases tab.
2. Run the program. You will be prompted to enter your **Lichess API Token** (You must create an API token with "Bot: Play games" permissions).
3. Ensure your Lichess account is officially upgraded to a BOT account (Warning: This action is irreversible on Lichess).
4. Click **Start Bot**.

## ⚙️ How it Works
The application uses a 2-part architecture:
- **C# GUI (`LichessBotGUI.exe`):** Handles the user interface, saving configurations securely to JSON, and passing commands.
- **Python Backend (`cli.py`, `bot.py`, `engine.py`):** Utilizes `berserk` (Lichess API wrapper) and `chess.engine` to stream game states, calculate moves using the Stockfish executable, and execute them on the Lichess servers with sub-millisecond latency. 

## ⚠️ Disclaimer & Ban Risk
**This software plays at a superhuman level.** 
Lichess strictly prohibits the use of computer assistance on standard human accounts. 
You **MUST** upgrade your account to a `BOT` account before running this software, otherwise you will be permanently IP-banned from the platform. Use at your own risk.

## 🛠️ Building from Source
**Prerequisites:**
- Python 3.10+
- .NET 9.0 SDK

```bash
# Clone the repo
git clone https://github.com/YourUsername/LichessBotV2.git
cd LichessBotV2

# Build the C# GUI
cd LichessBotGUI
dotnet build -c Release
```
