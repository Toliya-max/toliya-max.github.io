using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using System.Linq;
using System.Text.Json;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace LichessBotGUI
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Log entry model
    // ─────────────────────────────────────────────────────────────────────────
    public enum LogCategory { Info, Move, Game, Book, Warning, Error, System }

    public class LogEntry : INotifyPropertyChanged
    {
        public string Timestamp  { get; set; } = "";
        public string Icon       { get; set; } = "";
        public string Message    { get; set; } = "";
        public LogCategory Category { get; set; } = LogCategory.Info;

        // Colours derived from category — used directly in bindings.
        public Brush AccentStrip => Category switch
        {
            LogCategory.Move    => new SolidColorBrush(Color.FromRgb(0xb5, 0x88, 0x63)),  // #b58863 Accent
            LogCategory.Game    => new SolidColorBrush(Color.FromRgb(0x62, 0x99, 0x24)),  // #629924 Green
            LogCategory.Book    => new SolidColorBrush(Color.FromRgb(0xd4, 0xa7, 0x6a)),  // #d4a76a AccentLight
            LogCategory.Warning => new SolidColorBrush(Color.FromRgb(0xe3, 0x9a, 0x00)),  // amber
            LogCategory.Error   => new SolidColorBrush(Color.FromRgb(0xc9, 0x37, 0x2c)),  // #c9372c Red
            LogCategory.System  => new SolidColorBrush(Color.FromRgb(0x4a, 0x8a, 0xc4)),  // steel blue
            _                   => new SolidColorBrush(Color.FromRgb(0x33, 0x2f, 0x2c)),  // #332f2c Border (subtle)
        };

        public Brush MessageColor => Category switch
        {
            LogCategory.Move    => new SolidColorBrush(Color.FromRgb(0xe8, 0xe6, 0xe3)),  // TextPrimary
            LogCategory.Game    => new SolidColorBrush(Color.FromRgb(0x7e, 0xaf, 0x85)),  // GreenLight
            LogCategory.Book    => new SolidColorBrush(Color.FromRgb(0xd4, 0xa7, 0x6a)),  // AccentLight
            LogCategory.Warning => new SolidColorBrush(Color.FromRgb(0xe3, 0x9a, 0x00)),  // amber
            LogCategory.Error   => new SolidColorBrush(Color.FromRgb(0xf8, 0x51, 0x49)),  // RedLight
            LogCategory.System  => new SolidColorBrush(Color.FromRgb(0x7e, 0xaf, 0xd4)),  // light steel blue
            _                   => new SolidColorBrush(Color.FromRgb(0x8a, 0x87, 0x84)),  // TextSecondary
        };

        public Brush RowBackground => Category switch
        {
            LogCategory.Error   => new SolidColorBrush(Color.FromArgb(0x18, 0xc9, 0x37, 0x2c)),
            LogCategory.Move    => new SolidColorBrush(Color.FromArgb(0x10, 0xb5, 0x88, 0x63)),
            LogCategory.Game    => new SolidColorBrush(Color.FromArgb(0x10, 0x62, 0x99, 0x24)),
            _                   => new SolidColorBrush(Color.FromArgb(0x00, 0x00, 0x00, 0x00)),
        };

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Main window
    // ─────────────────────────────────────────────────────────────────────────
    public partial class MainWindow : Window
    {
        private const string CurrentVersion = "1.1.0";
        private const string GithubRepo = "Toliya-max/lichess-bot";

        private Process? _botProcess;
        private bool _isRunning;
        private ObservableCollection<LogEntry> _logEntries = new();
        private DispatcherTimer? _toastTimer;

        // Path to the Python bot directory
        // Use Environment.ProcessPath to guarantee we get the true location of the .exe 
        // completely ignoring what the shortcut's "Start In" folder might be set to.
        private string BotDirectory 
        {
            get 
            {
                string exePath = Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
                string exeDir = Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory;
                
                // Search upwards for cli.py
                string? currentDir = exeDir;
                while (currentDir != null)
                {
                    if (File.Exists(Path.Combine(currentDir, "cli.py")))
                    {
                        return currentDir;
                    }
                    currentDir = Directory.GetParent(currentDir)?.FullName;
                }
                
                // Fallback (shouldn't really happen if installed correctly)
                return exeDir;
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            ActivityList.ItemsSource = _logEntries;
            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            _toastTimer.Tick += (s, e) => { ToastBorder.Visibility = Visibility.Collapsed; _toastTimer.Stop(); };
            AddLog($"Lichess Bot Controller v{CurrentVersion} — Ready.", LogCategory.System);
            AddLog($"Bot directory: {BotDirectory}", LogCategory.System);
            LoadSettings();
            LoadToken();
            WriteVersionFile();
            _ = CheckForUpdatesAsync(silent: true);
        }

        // ════════════════════════════════════════════
        //  VERSION FILE
        // ════════════════════════════════════════════
        private void WriteVersionFile()
        {
            try
            {
                string versionPath = Path.Combine(BotDirectory, "version.txt");
                File.WriteAllText(versionPath, CurrentVersion);
            }
            catch { }
        }

        // ════════════════════════════════════════════
        //  START / STOP
        // ════════════════════════════════════════════
        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning) return;

            SaveSettings(); // Save all UI parameters to JSON before starting
            SaveToken();
            var args = BuildArgs();
            
            // Allow using the uninstaller's venv if available
            string pythonPath = "python";
            string venvPath = System.IO.Path.Combine(BotDirectory, "venv", "Scripts", "python.exe");
            if (System.IO.File.Exists(venvPath))
            {
                pythonPath = venvPath;
            }

            AppendLog($"\n>>> {pythonPath} -u cli.py {args}\n");

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = pythonPath,
                    Arguments = $"-u cli.py {args}",
                    WorkingDirectory = BotDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                _botProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
                _botProcess.OutputDataReceived += OnOutputData;
                _botProcess.ErrorDataReceived += OnOutputData;
                _botProcess.Exited += OnProcessExited;

                _botProcess.Start();
                _botProcess.BeginOutputReadLine();
                _botProcess.BeginErrorReadLine();

                _isRunning = true;
                BtnStart.IsEnabled = false;
                BtnStop.IsEnabled = true;
                SetStatus("Running", "#FF3FB950");
            }
            catch (Exception ex)
            {
                AppendLog($"ERROR: Failed to start bot process: {ex.Message}\n");
            }
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            StopBot();
        }

        private void StopBot()
        {
            if (_botProcess != null && !_botProcess.HasExited)
            {
                AppendLog("\n>>> Sending stop signal...\n");
                try
                {
                    // Kill the process tree
                    _botProcess.Kill(entireProcessTree: true);
                }
                catch { }
            }

            _isRunning = false;
            Dispatcher.Invoke(() =>
            {
                BtnStart.IsEnabled = true;
                BtnStop.IsEnabled = false;
                SetStatus("Stopped", "#FFF85149");
            });
        }

        // ════════════════════════════════════════════
        //  PROCESS I/O
        // ════════════════════════════════════════════
        private void OnOutputData(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                Dispatcher.Invoke(() =>
                {
                    AppendLog(e.Data + "\n");

                    // Parse game count from log lines like "Played: 3/10"
                    if (e.Data.Contains("Played:"))
                    {
                        var idx = e.Data.IndexOf("Played:");
                        var sub = e.Data.Substring(idx);
                        LblGames.Text = sub.TrimEnd(')');
                    }
                    
                    // Parse Eval from log lines like "Engine selected move: e2e4 (Eval: +0.45)"
                    if (e.Data.Contains("(Eval: "))
                    {
                        try
                        {
                            var evalIdx = e.Data.IndexOf("(Eval: ") + 7;
                            var colorIdx = e.Data.IndexOf(" | Color: ", evalIdx);
                            var endIdx = e.Data.IndexOf(")", evalIdx);

                            if (colorIdx > evalIdx && endIdx > colorIdx)
                            {
                                string evalStr = e.Data.Substring(evalIdx, colorIdx - evalIdx);
                                string colorStr = e.Data.Substring(colorIdx + 10, endIdx - (colorIdx + 10)).Trim();
                                
                                double eval = 0;
                                bool isMate = false;

                                if (evalStr.StartsWith("M") || evalStr.StartsWith("-M"))
                                {
                                    isMate = true;
                                    LblEval.Text = evalStr;
                                    eval = evalStr.StartsWith("-") ? -10 : 10;
                                }
                                else if (double.TryParse(evalStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedEval))
                                {
                                    eval = parsedEval;
                                    LblEval.Text = (eval > 0 ? "+" : "") + eval.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                                }

                                // If bot is playing black, invert the evaluation so the bar fills from the bot's perspective
                                double displayEval = colorStr.Equals("black", StringComparison.OrdinalIgnoreCase) ? -eval : eval;

                                // Cap eval to [-10, +10] for the bar visuals
                                double clamped = Math.Max(-10, Math.Min(10, displayEval));
                                
                                // Map -10 to 0% fill, +10 to 100% fill
                                double fillPct = (clamped + 10) / 20.0 * 100.0;
                                double emptyPct = 100.0 - fillPct;
                                
                                EvalWhiteCol.Width = new GridLength(fillPct, GridUnitType.Star);
                                EvalBlackCol.Width = new GridLength(emptyPct, GridUnitType.Star);
                            }
                        }
                        catch { }
                    }
                });
            }
        }

        private void OnProcessExited(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                AppendLog("\n--- Bot process exited ---\n");
                _isRunning = false;
                BtnStart.IsEnabled = true;
                BtnStop.IsEnabled = false;
                SetStatus("Exited", "#FF8B949E");
            });
        }

        // ════════════════════════════════════════════
        //  UI SETTINGS PERSISTENCE
        // ════════════════════════════════════════════
        private string SettingsPath => Path.Combine(BotDirectory, "settings.json");

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    var config = JsonSerializer.Deserialize<BotSettings>(json);
                    if (config != null)
                    {
                        ChkChallenger.IsChecked = config.AutoChallenger;
                        ChkRated.IsChecked = config.Rated;
                        ChkAutoResign.IsChecked = config.AutoResign;
                        TxtResignThreshold.Text = config.ResignThreshold ?? "-5.0";
                        TxtRating.Text = config.MinRating ?? "1900";
                        TxtMaxGames.Text = config.MaxGames ?? "0";
                        TxtMinutes.Text = config.BaseTime ?? "3";
                        TxtIncrement.Text = config.Increment ?? "0";
                        TxtEnginePath.Text = config.EnginePath ?? "Default Stockfish 18";
                        TxtBookPath.Text = config.BookPath ?? "Default gm_openings.bin";
                        ChkNNUE.IsChecked = config.UseNNUE;
                        SliderSkill.Value = config.SkillLevel;
                        SliderSpeed.Value = config.MoveSpeed;
                        SliderDepth.Value = double.TryParse(config.MaxDepth, out double d) ? d : 0;
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[Warning] Failed to load settings: {ex.Message}\n");
            }
        }

        private void SaveSettings()
        {
            try
            {
                var config = new BotSettings
                {
                    AutoChallenger = ChkChallenger.IsChecked == true,
                    Rated = ChkRated.IsChecked == true,
                    AutoResign = ChkAutoResign.IsChecked == true,
                    ResignThreshold = TxtResignThreshold.Text,
                    MinRating = TxtRating.Text,
                    MaxGames = TxtMaxGames.Text,
                    BaseTime = TxtMinutes.Text,
                    Increment = TxtIncrement.Text,
                    EnginePath = TxtEnginePath.Text,
                    BookPath = TxtBookPath.Text,
                    UseNNUE = ChkNNUE.IsChecked == true,
                    SkillLevel = SliderSkill.Value,
                    MoveSpeed = SliderSpeed.Value,
                    MaxDepth = ((int)SliderDepth.Value).ToString()
                };
                
                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex)
            {
                AppendLog($"[Warning] Failed to save settings: {ex.Message}\n");
            }
        }

        // ════════════════════════════════════════════
        //  API TOKEN HANDLING
        // ════════════════════════════════════════════
        private void LoadToken()
        {
            string envPath = Path.Combine(BotDirectory, ".env");
            if (File.Exists(envPath))
            {
                var lines = File.ReadAllLines(envPath);
                foreach (var line in lines)
                {
                    if (line.StartsWith("LICHESS_API_TOKEN="))
                    {
                        TxtApiToken.Text = line.Substring("LICHESS_API_TOKEN=".Length).Trim(' ', '"', '\'');
                        return;
                    }
                }
            }
        }

        private void SaveToken()
        {
            string token = TxtApiToken.Text.Trim();
            if (string.IsNullOrEmpty(token)) return;

            string envPath = Path.Combine(BotDirectory, ".env");
            if (!File.Exists(envPath))
            {
                File.WriteAllText(envPath, $"LICHESS_API_TOKEN={token}\n");
            }
            else
            {
                var lines = File.ReadAllLines(envPath).ToList();
                bool found = false;
                for (int i = 0; i < lines.Count; i++)
                {
                    if (lines[i].StartsWith("LICHESS_API_TOKEN="))
                    {
                        lines[i] = $"LICHESS_API_TOKEN={token}";
                        found = true;
                        break;
                    }
                }
                if (!found) lines.Add($"LICHESS_API_TOKEN={token}");
                File.WriteAllLines(envPath, lines);
            }
        }

        // ════════════════════════════════════════════
        //  FILE BROWSING
        // ════════════════════════════════════════════
        private void BtnBrowseEngine_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.DefaultExt = ".exe";
            dlg.Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*";
            dlg.Title = "Select Chess Engine Executable";

            if (dlg.ShowDialog() == true)
            {
                TxtEnginePath.Text = dlg.FileName;
            }
        }

        private void BtnBrowseBook_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.DefaultExt = ".bin";
            dlg.Filter = "Opening Books (*.bin)|*.bin|All Files (*.*)|*.*";
            dlg.Title = "Select Opening Book";

            if (dlg.ShowDialog() == true)
            {
                TxtBookPath.Text = dlg.FileName;
            }
        }

        // ════════════════════════════════════════════
        //  BUILD CLI ARGS FROM CONTROLS
        // ════════════════════════════════════════════
        private string BuildArgs()
        {
            var parts = new System.Collections.Generic.List<string>();

            parts.Add($"--min-rating {TxtRating.Text.Trim()}");
            parts.Add($"--max-games {TxtMaxGames.Text.Trim()}");
            parts.Add($"--skill {(int)SliderSkill.Value}");
            parts.Add($"--depth {(int)SliderDepth.Value}");
            parts.Add($"--speed {SliderSpeed.Value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}");
            
            // Allow sub-minute formats (0.5 for 30s) ignoring system culture
            string minStr = TxtMinutes.Text.Trim().Replace(",", ".");
            if (string.IsNullOrEmpty(minStr)) minStr = "0";
            parts.Add($"--tc-minutes {minStr}");
            
            string incStr = TxtIncrement.Text.Trim();
            if (string.IsNullOrEmpty(incStr)) incStr = "0";
            parts.Add($"--tc-increment {incStr}");

            if (ChkRated.IsChecked == true)
                parts.Add("--rated");

            if (ChkChallenger.IsChecked != true)
                parts.Add("--no-challenger");
                
            if (ChkAutoResign.IsChecked == true)
            {
                string thresh = TxtResignThreshold.Text.Trim().Replace(",", ".");
                parts.Add($"--auto-resign --resign-threshold {thresh}");
            }
                
            parts.Add($"--threads {(int)SliderThreads.Value}");
            parts.Add($"--hash {(int)SliderHash.Value}");

            string overheadStr = TxtMoveOverhead.Text.Trim();
            if (!string.IsNullOrEmpty(overheadStr) && int.TryParse(overheadStr, out int overhead) && overhead >= 0)
                parts.Add($"--move-overhead {overhead}");

            if (TxtEnginePath.Text != "Default Stockfish 18" && !string.IsNullOrWhiteSpace(TxtEnginePath.Text))
            {
                parts.Add($"--engine-path \"{TxtEnginePath.Text}\"");
            }

            if (TxtBookPath.Text != "Default gm_openings.bin" && TxtBookPath.Text != "None" && !string.IsNullOrWhiteSpace(TxtBookPath.Text))
            {
                parts.Add($"--book-path \"{TxtBookPath.Text}\"");
            }

            if (ChkNNUE.IsChecked == false)
                parts.Add("--no-nnue");

            return string.Join(" ", parts);
        }

        // ════════════════════════════════════════════
        //  UI HELPERS — structured log
        // ════════════════════════════════════════════

        /// <summary>Categorise a raw text line from the bot process and add it to the log.</summary>
        private void AppendLog(string text)
        {
            string clean = text.TrimEnd('\r', '\n');
            if (string.IsNullOrWhiteSpace(clean)) return;
            AddLog(clean, ClassifyLine(clean));
        }

        private static LogCategory ClassifyLine(string line)
        {
            if (line.Contains("Engine selected move", StringComparison.OrdinalIgnoreCase)  ||
                line.Contains("Successfully played move", StringComparison.OrdinalIgnoreCase))
                return LogCategory.Move;

            if (line.Contains("[BOOK]", StringComparison.OrdinalIgnoreCase))
                return LogCategory.Book;

            if (line.Contains("Game started", StringComparison.OrdinalIgnoreCase)   ||
                line.Contains("Starting game thread", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Game ", StringComparison.OrdinalIgnoreCase) && line.Contains("thread completed", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Accepting challenge", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Declining challenge", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Bot process exited", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Challenger:", StringComparison.OrdinalIgnoreCase))
                return LogCategory.Game;

            if (line.Contains("WARNING", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Rate limited", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Reconnecting", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Retrying", StringComparison.OrdinalIgnoreCase))
                return LogCategory.Warning;

            if (line.Contains("ERROR", StringComparison.OrdinalIgnoreCase)     ||
                line.Contains("Exception", StringComparison.OrdinalIgnoreCase)  ||
                line.Contains("Traceback", StringComparison.OrdinalIgnoreCase)  ||
                line.Contains("failed", StringComparison.OrdinalIgnoreCase) && line.Contains("attempt", StringComparison.OrdinalIgnoreCase))
                return LogCategory.Error;

            if (line.StartsWith("===") || line.StartsWith("---") ||
                line.Contains("[UNLEASHED", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("[HARDWARE", StringComparison.OrdinalIgnoreCase)  ||
                line.Contains("[NNUE",     StringComparison.OrdinalIgnoreCase)  ||
                line.Contains("Bot is listening", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Bot directory", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Controller — Ready", StringComparison.OrdinalIgnoreCase))
                return LogCategory.System;

            return LogCategory.Info;
        }

        private static string IconForCategory(LogCategory cat) => cat switch
        {
            LogCategory.Move    => "♟",
            LogCategory.Game    => "♞",
            LogCategory.Book    => "📖",
            LogCategory.Warning => "⚠",
            LogCategory.Error   => "✖",
            LogCategory.System  => "⚙",
            _                   => "·",
        };

        private void AddLog(string message, LogCategory category = LogCategory.Info)
        {
            var entry = new LogEntry
            {
                Timestamp = DateTime.Now.ToString("HH:mm:ss"),
                Icon      = IconForCategory(category),
                Message   = message,
                Category  = category,
            };

            _logEntries.Add(entry);

            // Keep the last 200 entries max to avoid memory growth.
            while (_logEntries.Count > 200)
                _logEntries.RemoveAt(0);

            // Auto-scroll to the newest entry.
            if (ActivityList.Items.Count > 0)
                ActivityList.ScrollIntoView(ActivityList.Items[ActivityList.Items.Count - 1]);

            // Show toast for errors.
            if (category == LogCategory.Error)
                ShowToast(message);
        }

        // Keep a convenience alias so all the preset/button handlers that call AddActivity keep working.
        private void AddActivity(string message) => AddLog(message, ClassifyLine(message));

        private void ShowToast(string message)
        {
            TxtToast.Text = message;
            ToastBorder.Visibility = Visibility.Visible;
            _toastTimer?.Stop();
            _toastTimer?.Start();
        }

        private void SetStatus(string text, string hexColor)
        {
            LblStatus.Text = text;
            var color = (Color)ColorConverter.ConvertFromString(hexColor);
            StatusDot.Fill = new SolidColorBrush(color);
        }

        private void SliderSkill_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LblSkill != null) LblSkill.Text = ((int)e.NewValue).ToString();
        }

        private void SliderSpeed_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LblSpeed != null) LblSpeed.Text = $"{e.NewValue.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}x";
        }

        private void SliderDepth_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LblDepth != null) 
            {
                int val = (int)e.NewValue;
                LblDepth.Text = val == 0 ? "0 (∞)" : val.ToString();
            }
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Retry clipboard copy to prevent COMException
                for (int i = 0; i < 5; i++)
                {
                    try
                    {
                        Clipboard.SetText(string.Join("\n", _logEntries.Select(e => $"[{e.Timestamp}] {e.Message}")));
                        AddActivity("📋 Logs copied to clipboard!");
                        return;
                    }
                    catch (System.Runtime.InteropServices.COMException)
                    {
                        System.Threading.Thread.Sleep(100);
                    }
                }
                AddActivity("⚠ Failed to set clipboard (locked).");
            }
            catch (Exception ex)
            {
                AddActivity($"⚠ Error copying: {ex.Message}");
            }
        }

        private async void BtnResignAll_Click(object sender, RoutedEventArgs e)
        {
            string token = TxtApiToken.Text.Trim();
            if (string.IsNullOrEmpty(token))
            {
                ShowToast("API Token is required to resign games.");
                return;
            }

            BtnResignAll.IsEnabled = false;
            AddActivity("🏳 Starting 'Resign All' process...");

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                // 1. Get ongoing games
                var response = await client.GetAsync("https://lichess.org/api/account/playing");
                if (!response.IsSuccessStatusCode)
                {
                    AddActivity($"ERROR: Failed to fetch ongoing games ({response.StatusCode})");
                    return;
                }

                var content = await response.Content.ReadAsStringAsync();
                var playingData = JsonSerializer.Deserialize<LichessPlayingResponse>(content);

                if (playingData?.NowPlaying == null || playingData.NowPlaying.Count == 0)
                {
                    AddActivity("No active games found to resign.");
                    return;
                }

                AddActivity($"Found {playingData.NowPlaying.Count} active games. Resigning...");

                foreach (var game in playingData.NowPlaying)
                {
                    var resignUrl = $"https://lichess.org/api/bot/game/{game.GameId}/resign";
                    var resignResponse = await client.PostAsync(resignUrl, null);
                    if (resignResponse.IsSuccessStatusCode)
                    {
                        AddActivity($"Successfully resigned game: {game.GameId}");
                    }
                    else
                    {
                        AddActivity($"Failed to resign game {game.GameId}: {resignResponse.StatusCode}");
                    }
                }
                AddActivity("🏳 'Resign All' process completed.");
            }
            catch (Exception ex)
            {
                AddActivity($"ERROR during Resign All: {ex.Message}");
            }
            finally
            {
                BtnResignAll.IsEnabled = true;
            }
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            _logEntries.Clear();
            AddLog("Console cleared.", LogCategory.System);
        }

        // ════════════════════════════════════════════
        //  UPDATE CHECK
        // ════════════════════════════════════════════
        private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            BtnCheckUpdates.IsEnabled = false;
            await CheckForUpdatesAsync(silent: false);
            BtnCheckUpdates.IsEnabled = true;
        }

        private async Task CheckForUpdatesAsync(bool silent)
        {
            // Version check via jsDelivr CDN — works even where GitHub is blocked
            string[] checkUrls =
            {
                "https://gist.githubusercontent.com/Toliya-max/17c837a5b5a108b5f85b76c3d8dcf9a9/raw/version.txt",
            };

            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("LichessBotGUI/1.0");

                string latestVersion = "";
                foreach (string url in checkUrls)
                {
                    try
                    {
                        latestVersion = (await client.GetStringAsync(url)).Trim().TrimStart('v');
                        if (!string.IsNullOrEmpty(latestVersion)) break;
                    }
                    catch { }
                }

                if (string.IsNullOrEmpty(latestVersion))
                {
                    if (!silent) AddLog("Could not reach update server.", LogCategory.Warning);
                    return;
                }

                if (!IsNewerVersion(latestVersion, CurrentVersion))
                {
                    if (!silent) AddLog($"Already up to date (v{CurrentVersion}).", LogCategory.System);
                    return;
                }

                AddLog($"Update available: v{latestVersion} (current: v{CurrentVersion})", LogCategory.Warning);

                string downloadPage = $"https://github.com/{GithubRepo}/releases/latest";
                var result = MessageBox.Show(
                    $"Version v{latestVersion} is available!\n\nDownload the new installer from the releases page?",
                    "Update Available",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                    Process.Start(new ProcessStartInfo(downloadPage) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                if (!silent) AddLog($"Update check error: {ex.Message}", LogCategory.Error);
            }
        }

        private static bool IsNewerVersion(string latest, string current)
        {
            if (Version.TryParse(latest, out var v1) && Version.TryParse(current, out var v2))
                return v1 > v2;
            return string.Compare(latest, current, StringComparison.Ordinal) > 0;
        }

        // ════════════════════════════════════════════
        //  UI PRESETS
        // ════════════════════════════════════════════
        private void BtnPresetBullet_Click(object sender, RoutedEventArgs e)
        {
            CustomTimePanel.Visibility = Visibility.Collapsed;
            TxtMinutes.Text = "1";
            TxtIncrement.Text = "0";
            AppendLog("⚡ Preset loaded: Bullet 1+0\n");
        }

        private void BtnPresetBlitz_Click(object sender, RoutedEventArgs e)
        {
            CustomTimePanel.Visibility = Visibility.Collapsed;
            TxtMinutes.Text = "3";
            TxtIncrement.Text = "0";
            AppendLog("⏱ Preset loaded: Blitz 3+0\n");
        }

        private void BtnPresetRapid_Click(object sender, RoutedEventArgs e)
        {
            CustomTimePanel.Visibility = Visibility.Collapsed;
            TxtMinutes.Text = "10";
            TxtIncrement.Text = "0";
            AppendLog("🐢 Preset loaded: Rapid 10+0\n");
        }

        private void BtnPresetHyper_Click(object sender, RoutedEventArgs e)
        {
            CustomTimePanel.Visibility = Visibility.Collapsed;
            TxtMinutes.Text = "0.5";
            TxtIncrement.Text = "0";
            AddActivity("💥 Preset loaded: HyperBullet 0.5+0");
        }

        private void BtnPresetClassical_Click(object sender, RoutedEventArgs e)
        {
            CustomTimePanel.Visibility = Visibility.Collapsed;
            TxtMinutes.Text = "15";
            TxtIncrement.Text = "10";
            AddActivity("🏛 Preset loaded: Classical 15+10");
        }

        private void BtnPresetCustom_Click(object sender, RoutedEventArgs e)
        {
            CustomTimePanel.Visibility = Visibility.Visible;
            TxtMinutes.Text = "";
            TxtIncrement.Text = "";
            AddActivity("✏ Custom mode — enter your own time control values");
        }

        private void SliderThreads_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LblThreads != null) LblThreads.Text = ((int)e.NewValue).ToString();
        }

        private void SliderHash_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LblHash != null) LblHash.Text = ((int)e.NewValue).ToString();
        }

        protected override void OnClosed(EventArgs e)
        {
            SaveSettings();
            StopBot();
            base.OnClosed(e);
        }
    }

    public class BotSettings
    {
        public bool AutoChallenger { get; set; } = true;
        public bool Rated { get; set; } = false;
        public bool AutoResign { get; set; } = true;
        public string ResignThreshold { get; set; } = "-5.0";
        public string MinRating { get; set; } = "1900";
        public string MaxGames { get; set; } = "0";
        public string BaseTime { get; set; } = "3";
        public string Increment { get; set; } = "0";
        public string EnginePath { get; set; } = "Default Stockfish 18";
        public string BookPath { get; set; } = "Default gm_openings.bin";
        public bool UseNNUE { get; set; } = true;
        public double SkillLevel { get; set; } = 20;
        public double MoveSpeed { get; set; } = 1.0;
        public string MaxDepth { get; set; } = "0";
        // V3 Advanced
        public bool Ponder { get; set; } = false;
        public int Threads { get; set; } = 4;
        public int Hash { get; set; } = 256;
        public string MoveOverhead { get; set; } = "100";
        public int VariantIndex { get; set; } = 0;
        public int ColorIndex { get; set; } = 0;
        public bool SendChat { get; set; } = true;
        public string Greeting { get; set; } = "glhf! 🤖";
        public string GGMessage { get; set; } = "gg wp!";
        public bool AcceptRematch { get; set; } = true;
    }

    public class LichessPlayingResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("nowPlaying")]
        public System.Collections.Generic.List<LichessGameInfo>? NowPlaying { get; set; }
    }

    public class LichessGameInfo
    {
        [System.Text.Json.Serialization.JsonPropertyName("gameId")]
        public string? GameId { get; set; }
    }
}