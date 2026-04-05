# Lichess Bot — Demo Video Script

**Target duration:** 3–4 minutes  
**Format:** screen recording (1920x1080, 60fps) + voiceover  
**Tool:** OBS Studio or ShareX (screen capture), DaVinci Resolve or CapCut (editing)

---

## Scene 1 — Installation (0:00–0:35)

**What to show:**
1. Open the GitHub Releases page: `https://github.com/Toliya-max/lichess-bot/releases/latest`
2. Click the installer asset (`LichessBotSetup.exe`) — browser downloads it.
3. Run the installer — Windows UAC prompt appears, click Yes.
4. Installer wizard: Next → Next → Install. Progress bar fills, then "Finish".
5. Desktop shortcut appears. Double-click it.

**Voiceover:**
> "Download and run the installer from the releases page. The setup takes about 30 seconds and includes everything — Python, Stockfish 18, and all dependencies."

---

## Scene 2 — License Activation (0:35–1:05)

**What to show:**
1. Lichess Bot Controller opens, then the **Activation Window** appears automatically.
2. Paste a valid license key into the input field (have one ready before filming).
3. Click **Activate** — brief "Validating key..." message, then green "Activated! Monthly — expires YYYY-MM-DD (30 days)".
4. Activation window closes. Main window is now visible.
5. Bottom of Controls tab shows `LblLicenseStatus` with license info.

**Voiceover:**
> "On first launch, the activation window appears. Enter your license key — monthly or yearly plans are available. The key is verified and stored locally."

---

## Scene 3 — Configuration (1:05–1:45)

**What to show:**
1. **Settings tab:** paste a Lichess API token into the token field.
2. Toggle **Auto Challenger** on, **Rated Matches** on.
3. **Controls tab:** click **3+0** blitz preset — preset button highlights.
4. **Engine tab:** show Skill Level slider at 20, NNUE on.
5. **Advanced tab:** CPU Threads = 4, Hash = 256 MB.

**Voiceover:**
> "Enter your Lichess API token, choose a time control preset, and tune the engine. Skill level 20 with NNUE gives maximum strength."

---

## Scene 4 — Starting the Bot (1:45–2:15)

**What to show:**
1. Click the green **Start Bot** button.
2. Live Feed panel fills with log lines:
   - System lines: `=== Lichess Bot CLI ===`, `[LICENSE] Monthly license active`
   - Game line: `Bot is listening for challenges...`
   - Accept/decline lines as challenges arrive.
3. Status badge in the top-right changes from "Ready" to **Running** (green dot).
4. A game is accepted — `Game started` log entry appears.

**Voiceover:**
> "Hit Start Bot. The bot connects to Lichess and starts accepting challenges immediately. All activity is shown in the live feed."

---

## Scene 5 — Live Game (2:15–3:10)

**What to show:**
1. Open the Lichess game URL from the log (or open `lichess.org` in a browser, switch to the active game).
2. The bot plays moves in real time — each move appears as a log card in the Live Feed.
3. The **Eval Bar** at the bottom moves with each move (white/black advantage).
4. Log shows `[BOOK] Opening move: e2e4` for the first few moves, then `Engine selected move: d7d5 (Eval: +0.15 | Color: white)`.
5. Game ends — `Game thread completed` log entry.

**Voiceover:**
> "Watch the bot play in real time. The eval bar updates after every move, and book moves are highlighted separately."

---

## Scene 6 — Statistics (3:10–3:40)

**What to show:**
1. In the Live Feed, scroll to see `Played: 1/unlimited` line.
2. Open `stats.json` in a text editor — show wins/losses/draws counter.
   *(Optional: if a Stats tab is added in a future version, show it instead.)*
3. Click **Stop Bot** — status changes to "Stopped" (red dot), bot exits cleanly.

**Voiceover:**
> "The bot tracks every game. Stats are saved locally in stats.json. Stop the bot at any time with a single click."

---

## Scene 7 — Outro (3:40–4:00)

**What to show:**
1. Back to the main UI — still, clean shot.
2. Fade to a title card: "Lichess Bot — available on GitHub" with the repo URL.

**Voiceover:**
> "Lichess Bot — powerful, configurable, and easy to set up. Get your license and start playing today."

---

## Recording Checklist

- [ ] Resolution: 1920x1080, 60fps
- [ ] Font scaling: 100% (no HiDPI blurriness)
- [ ] Have a valid test license key ready before filming Scene 2
- [ ] Have a Lichess bot-account API token ready for Scene 3
- [ ] Disable Windows notifications (Focus Assist: Priority Only)
- [ ] Start OBS/ShareX capture before launching the installer
- [ ] Record voiceover separately and sync in editor
- [ ] Export: H.264, CRF 18, 1920x1080, YouTube preset

---

## Suggested Editing Notes

- Add a subtle zoom-in on the Activation Window in Scene 2 to make the key input readable.
- Add a green checkmark overlay animation when "Activated!" appears.
- Speed up installer wizard (Scene 1) to 2x — no one wants to watch a progress bar at 1x.
- Add chapter markers for YouTube: Installation / Activation / Configuration / Live Game / Stats.
