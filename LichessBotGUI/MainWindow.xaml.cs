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
            LogCategory.Move    => new SolidColorBrush(Color.FromRgb(0xd4, 0x98, 0x5a)),
            LogCategory.Game    => new SolidColorBrush(Color.FromRgb(0x6a, 0x9b, 0x2c)),
            LogCategory.Book    => new SolidColorBrush(Color.FromRgb(0xe8, 0xb8, 0x7a)),
            LogCategory.Warning => new SolidColorBrush(Color.FromRgb(0xe0, 0x9a, 0x20)),
            LogCategory.Error   => new SolidColorBrush(Color.FromRgb(0xb8, 0x38, 0x28)),
            LogCategory.System  => new SolidColorBrush(Color.FromRgb(0x8a, 0x70, 0x50)),
            _                   => new SolidColorBrush(Color.FromRgb(0x3a, 0x2c, 0x20)),
        };

        public Brush MessageColor => Category switch
        {
            LogCategory.Move    => new SolidColorBrush(Color.FromRgb(0xf0, 0xeb, 0xe4)),
            LogCategory.Game    => new SolidColorBrush(Color.FromRgb(0x8a, 0xb8, 0x6a)),
            LogCategory.Book    => new SolidColorBrush(Color.FromRgb(0xe8, 0xb8, 0x7a)),
            LogCategory.Warning => new SolidColorBrush(Color.FromRgb(0xe0, 0x9a, 0x20)),
            LogCategory.Error   => new SolidColorBrush(Color.FromRgb(0xe8, 0x50, 0x40)),
            LogCategory.System  => new SolidColorBrush(Color.FromRgb(0xa0, 0x88, 0x68)),
            _                   => new SolidColorBrush(Color.FromRgb(0xa0, 0x92, 0x82)),
        };

        public Brush RowBackground => Category switch
        {
            LogCategory.Error   => new SolidColorBrush(Color.FromArgb(0x18, 0xb8, 0x38, 0x28)),
            LogCategory.Move    => new SolidColorBrush(Color.FromArgb(0x0c, 0xd4, 0x98, 0x5a)),
            LogCategory.Game    => new SolidColorBrush(Color.FromArgb(0x0c, 0x6a, 0x9b, 0x2c)),
            _                   => new SolidColorBrush(Color.FromArgb(0x00, 0x00, 0x00, 0x00)),
        };

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Main window
    // ─────────────────────────────────────────────────────────────────────────
    public partial class MainWindow : Window
    {
        private const string CurrentVersion = "1.4.2";
        private const string GithubRepo = "Toliya-max/lichess-bot";

        private Process? _botProcess;
        private bool _isRunning;
        private ObservableCollection<LogEntry> _logEntries = new();
        private ObservableCollection<LogEntry> _recentMoves = new();
        private DispatcherTimer? _toastTimer;
        private int _wins, _draws, _losses, _gamesPlayed;

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
            RecentMovesList.ItemsSource = _recentMoves;
            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            _toastTimer.Tick += (s, e) => { ToastBorder.Visibility = Visibility.Collapsed; _toastTimer.Stop(); };
            AddLog($"Lichess Bot Controller v{CurrentVersion} — Ready.", LogCategory.System);
            AddLog($"Bot directory: {BotDirectory}", LogCategory.System);
            DetectHardware();
            LoadSettings();
            LoadToken();
            WriteVersionFile();
            _ = CheckForUpdatesAsync(silent: true);
            Loaded += MainWindow_Loaded;
        }

        private void DetectHardware()
        {
            int cpuCores = Environment.ProcessorCount;
            long totalRamMB = (long)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024);
            int maxHash = (int)Math.Min(totalRamMB / 2, 16384);
            maxHash = Math.Max(maxHash, 64);

            SliderThreads.Maximum = cpuCores;
            SliderHash.Maximum = maxHash;
            LblThreadsMax.Text = $"/ {cpuCores}";
            LblHashMax.Text = $"/ {maxHash}";

            AddLog($"[HARDWARE] CPU: {cpuCores} cores, RAM: {totalRamMB} MB", LogCategory.System);
            AddLog($"[HARDWARE] Limits: Threads 1-{cpuCores}, Hash 16-{maxHash} MB", LogCategory.System);
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await CheckLicenseAsync();
        }

        // ════════════════════════════════════════════
        //  LICENSE
        // ════════════════════════════════════════════

        private string PythonPath
        {
            get
            {
                string venv = Path.Combine(BotDirectory, "venv", "Scripts", "python.exe");
                return File.Exists(venv) ? venv : "python";
            }
        }

        private async Task CheckLicenseAsync(bool forceShowWindow = false)
        {
            var result = await Task.Run(() => RunLicenseCheck());

            if (result.Valid)
            {
                AddLog($"[LICENSE] {result.Info}", LogCategory.System);
                if (LblLicenseStatus != null)
                    LblLicenseStatus.Text = result.Info ?? "Active";
                if (!forceShowWindow) return;
            }
            else
            {
                if (result.Error != null)
                    AddLog($"[LICENSE] {result.Error}", LogCategory.Warning);
                if (LblLicenseStatus != null)
                    LblLicenseStatus.Text = result.Error ?? "Not validated";
                if (!forceShowWindow) return;
            }

            string currentApiToken = TxtApiToken.Text.Trim();
            var win = new ActivationWindow(
                PythonPath,
                BotDirectory,
                isManageMode: true,
                currentKey: result.Key,
                currentInfo: result.Valid ? result.Info : null,
                currentApiToken: currentApiToken);
            win.Owner = this;
            win.ShowDialog();

            if (!win.IsActivated) return;

            LoadToken();
            var recheck = await Task.Run(() => RunLicenseCheck());
            if (recheck.Valid)
            {
                AddLog($"[LICENSE] {recheck.Info}", LogCategory.System);
                if (LblLicenseStatus != null)
                    LblLicenseStatus.Text = recheck.Info ?? "Active";
            }
        }

        private record LicenseCheckResult(bool Valid, string? Info, string? Error, string? Key = null);

        private LicenseCheckResult RunLicenseCheck()
        {
            // Script returns: type — expires date (days)\nKEY
            string script =
                $"import sys; sys.path.insert(0, r'{BotDirectory}'); " +
                "import license as L; " +
                "info = L.check(); " +
                "print(f\"{info['type']} — expires {info['expiry']} ({info['days_left']} days)\"); " +
                "print(info.get('key', ''))";

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = PythonPath,
                    Arguments = $"-c \"{script.Replace("\"", "\\\"")}\"",
                    WorkingDirectory = BotDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8,
                    CreateNoWindow = true,
                };
                psi.Environment["PYTHONIOENCODING"] = "utf-8";
                psi.Environment["PYTHONUTF8"] = "1";

                using var proc = Process.Start(psi)!;
                string stdout = proc.StandardOutput.ReadToEnd().Trim();
                string stderr = proc.StandardError.ReadToEnd().Trim();
                proc.WaitForExit();

                if (proc.ExitCode == 0 && !string.IsNullOrEmpty(stdout))
                {
                    var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    string info = lines[0].Trim();
                    string? key = lines.Length > 1 ? lines[1].Trim() : null;
                    return new LicenseCheckResult(true, info, null, key);
                }

                string err = stderr;
                if (err.Contains("LicenseError:"))
                    err = err.Substring(err.LastIndexOf("LicenseError:") + "LicenseError:".Length).Trim();
                else if (err.Contains("ModuleNotFoundError"))
                    return new LicenseCheckResult(false, null, "License module missing. Reinstall required.");
                else if (string.IsNullOrEmpty(err) && proc.ExitCode != 0)
                    err = "License check failed";

                return new LicenseCheckResult(false, null, err);
            }
            catch (Exception ex)
            {
                return new LicenseCheckResult(true, $"License check skipped ({ex.Message})", null);
            }
        }

        private async void BtnLicense_Click(object sender, RoutedEventArgs e)
        {
            await CheckLicenseAsync(forceShowWindow: true);
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
        //  STOCKFISH ENGINE (lazy download on first start)
        // ════════════════════════════════════════════
        private const string StockfishUrl =
            "https://github.com/official-stockfish/Stockfish/releases/download/sf_18/stockfish-windows-x86-64-avx2.zip";

        private string EngineCacheDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LichessBot-cache", "stockfish");

        private void ShowWinToast(string title, string body, int timeoutMs = 6000)
        {
            try
            {
                string safeTitle = title.Replace("'", "''").Replace("\"", "");
                string safeBody = body.Replace("'", "''").Replace("\"", "");
                string ps =
                    "[void][Windows.UI.Notifications.ToastNotificationManager,Windows.UI.Notifications,ContentType=WindowsRuntime];" +
                    "[void][Windows.Data.Xml.Dom.XmlDocument,Windows.Data.Xml.Dom.XmlDocument,ContentType=WindowsRuntime];" +
                    "$x=New-Object Windows.Data.Xml.Dom.XmlDocument;" +
                    $"$x.LoadXml('<toast><visual><binding template=\"ToastGeneric\"><text>{safeTitle}</text><text>{safeBody}</text></binding></visual></toast>');" +
                    "$t=[Windows.UI.Notifications.ToastNotification]::new($x);" +
                    "[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('Lichess Bot').Show($t);";
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -WindowStyle Hidden -Command \"{ps}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                AppendLog($"[TOAST] {ex.Message}");
            }
        }

        private async Task<bool> EnsureEngineAsync()
        {
            string engineDir = Path.Combine(BotDirectory, "stockfish18");
            if (Directory.Exists(engineDir) &&
                Directory.GetFiles(engineDir, "stockfish*.exe", SearchOption.AllDirectories).Length > 0)
            {
                return true;
            }

            if (Directory.Exists(EngineCacheDir))
            {
                var cached = Directory.GetFiles(EngineCacheDir, "stockfish*.exe");
                if (cached.Length > 0)
                {
                    Directory.CreateDirectory(engineDir);
                    foreach (var src in cached)
                    {
                        string dst = Path.Combine(engineDir, Path.GetFileName(src));
                        File.Copy(src, dst, overwrite: true);
                    }
                    AppendLog($"[ENGINE] Restored from cache ({cached[0]})");
                    return true;
                }
            }

            AppendLog("[ENGINE] Stockfish not found, downloading...");
            SetStatus("Downloading engine", "#FFB58863");
            ShowWinToast("Lichess Bot",
                "Downloading chess engine (~77 MB). This runs in the background and happens only once.",
                8000);

            string zipPath = Path.Combine(Path.GetTempPath(), "lichess_stockfish.zip");
            string tempExtract = Path.Combine(Path.GetTempPath(), "lichess_stockfish_tmp");
            if (Directory.Exists(tempExtract))
            {
                try { Directory.Delete(tempExtract, true); } catch { }
            }

            try
            {
                using var http = new System.Net.Http.HttpClient();
                http.Timeout = TimeSpan.FromMinutes(10);
                using var response = await http.GetAsync(StockfishUrl,
                    System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? -1;
                using var src = await response.Content.ReadAsStreamAsync();
                using var dst = new FileStream(zipPath, FileMode.Create, FileAccess.Write,
                                               FileShare.None, 81920);
                byte[] buf = new byte[81920];
                long got = 0;
                int lastPct = -5;
                int read;
                while ((read = await src.ReadAsync(buf, 0, buf.Length)) > 0)
                {
                    await dst.WriteAsync(buf, 0, read);
                    got += read;
                    if (total > 0)
                    {
                        int pct = (int)(got * 100 / total);
                        if (pct - lastPct >= 5)
                        {
                            lastPct = pct;
                            AppendLog($"[ENGINE] {pct}%  ({got / 1024 / 1024}MB / {total / 1024 / 1024}MB)");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[ENGINE] Download failed: {ex.Message}");
                SetStatus("Stopped", "#FFD32F2F");
                return false;
            }

            try
            {
                AppendLog("[ENGINE] Extracting...");
                System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, tempExtract);
                Directory.CreateDirectory(engineDir);
                var sub = Directory.GetDirectories(tempExtract);
                string root = sub.Length > 0 ? sub[0] : tempExtract;
                foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                {
                    string rel = Path.GetRelativePath(root, file);
                    string to = Path.Combine(engineDir, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                    File.Move(file, to, true);
                }
                try { File.Delete(zipPath); } catch { }
                try { Directory.Delete(tempExtract, true); } catch { }

                try
                {
                    Directory.CreateDirectory(EngineCacheDir);
                    foreach (var exe in Directory.GetFiles(engineDir, "stockfish*.exe",
                                                           SearchOption.AllDirectories))
                    {
                        string dst = Path.Combine(EngineCacheDir, Path.GetFileName(exe));
                        File.Copy(exe, dst, overwrite: true);
                    }
                    AppendLog($"[ENGINE] Cached to {EngineCacheDir}");
                }
                catch (Exception ex) { AppendLog($"[ENGINE] Cache save failed: {ex.Message}"); }

                AppendLog("[ENGINE] Ready.");
                ShowWinToast("Lichess Bot", "Chess engine ready. Starting the bot.", 5000);
                return true;
            }
            catch (Exception ex)
            {
                AppendLog($"[ENGINE] Extract failed: {ex.Message}");
                SetStatus("Stopped", "#FFD32F2F");
                return false;
            }
        }

        // ════════════════════════════════════════════
        //  START / STOP
        // ════════════════════════════════════════════
        private async void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning) return;

            string cliPath = Path.Combine(BotDirectory, "cli.py");
            if (!File.Exists(cliPath))
            {
                MessageBox.Show(
                    $"cli.py not found at:\n{cliPath}\n\nPlease reinstall the bot using the latest Setup.",
                    "Installation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            BtnStart.IsEnabled = false;
            try
            {
                bool engineReady = await EnsureEngineAsync();
                if (!engineReady)
                {
                    MessageBox.Show(
                        "Could not prepare the chess engine. Check your internet connection and try again.",
                        "Engine Missing", MessageBoxButton.OK, MessageBoxImage.Error);
                    BtnStart.IsEnabled = true;
                    return;
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[ENGINE] {ex.Message}");
                BtnStart.IsEnabled = true;
                return;
            }

            SaveSettings();
            SaveToken();
            var args = BuildArgs();

            string pythonPath = "python";
            string venvPath = System.IO.Path.Combine(BotDirectory, "venv", "Scripts", "python.exe");
            if (System.IO.File.Exists(venvPath))
            {
                pythonPath = venvPath;
            }

            AppendLog($"{pythonPath} -u cli.py {args}");

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
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8,
                    CreateNoWindow = true,
                };
                psi.Environment["PYTHONIOENCODING"] = "utf-8";
                psi.Environment["PYTHONUTF8"] = "1";

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
                        LblGamesBottom.Text = sub.TrimEnd(')');
                    }
                    
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

                                bool isBotWhite = string.Equals(colorStr, "white", StringComparison.OrdinalIgnoreCase);

                                double whiteEval = 0;
                                bool isMate = false;
                                int? mateIn = null;
                                bool mateForWhite = false;

                                if (evalStr.StartsWith("M") || evalStr.StartsWith("-M"))
                                {
                                    isMate = true;
                                    mateForWhite = !evalStr.StartsWith("-");
                                    string num = evalStr.TrimStart('-').Substring(1);
                                    if (int.TryParse(num, out int mv)) mateIn = mv;
                                    whiteEval = mateForWhite ? 10 : -10;
                                }
                                else if (double.TryParse(evalStr,
                                    System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    out double parsedEval))
                                {
                                    whiteEval = parsedEval;
                                }

                                double botEval = isBotWhite ? whiteEval : -whiteEval;

                                if (isMate)
                                {
                                    bool mateForBot = (isBotWhite == mateForWhite);
                                    int n = mateIn ?? 0;
                                    LblEval.Text = (mateForBot ? "+#" : "-#") + n;
                                }
                                else
                                {
                                    LblEval.Text = (botEval >= 0 ? "+" : "")
                                        + botEval.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                                }

                                double clamped = Math.Max(-10, Math.Min(10, botEval));
                                double selfPct = (clamped + 10) / 20.0 * 100.0;
                                double oppPct = 100.0 - selfPct;

                                if (isBotWhite)
                                {
                                    EvalWhiteCol.Width = new GridLength(selfPct, GridUnitType.Star);
                                    EvalBlackCol.Width = new GridLength(oppPct, GridUnitType.Star);
                                }
                                else
                                {
                                    EvalBlackCol.Width = new GridLength(selfPct, GridUnitType.Star);
                                    EvalWhiteCol.Width = new GridLength(oppPct, GridUnitType.Star);
                                }
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
            if (ShouldFilterLine(clean)) return;
            AddLog(clean, ClassifyLine(clean));
        }

        private static bool ShouldFilterLine(string line)
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0) return true;
            if (trimmed.All(c => c == '=' || c == '-' || c == '~' || c == '*' || c == ' ')) return true;
            if (trimmed.StartsWith("===") || trimmed.StartsWith("---") || trimmed.StartsWith("~~~")) return true;
            if (trimmed.StartsWith(">>>")) return true;
            if (trimmed.StartsWith("Traceback ")) return true;
            if (trimmed.StartsWith("File \"") && trimmed.Contains("line ")) return true;
            if (trimmed.Length <= 2 && !char.IsLetterOrDigit(trimmed[0])) return true;
            return false;
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
            while (_logEntries.Count > 200)
                _logEntries.RemoveAt(0);
            if (ActivityList.Items.Count > 0)
                ActivityList.ScrollIntoView(ActivityList.Items[ActivityList.Items.Count - 1]);

            if (category == LogCategory.Move || category == LogCategory.Game || category == LogCategory.Book)
            {
                _recentMoves.Add(entry);
                while (_recentMoves.Count > 30)
                    _recentMoves.RemoveAt(0);
                if (RecentMovesList.Items.Count > 0)
                    RecentMovesList.ScrollIntoView(RecentMovesList.Items[RecentMovesList.Items.Count - 1]);
            }

            if (message.Contains("Stats: W="))
                ParseBotStats(message);

            if (category == LogCategory.Error)
                ShowToast(message);
        }

        // Keep a convenience alias so all the preset/button handlers that call AddActivity keep working.
        private void AddActivity(string message) => AddLog(message, ClassifyLine(message));

        private void ParseBotStats(string line)
        {
            try
            {
                var match = System.Text.RegularExpressions.Regex.Match(line, @"W=(\d+)\s+L=(\d+)\s+D=(\d+)\s+Total=(\d+)");
                if (match.Success)
                {
                    _wins = int.Parse(match.Groups[1].Value);
                    _losses = int.Parse(match.Groups[2].Value);
                    _draws = int.Parse(match.Groups[3].Value);
                    _gamesPlayed = int.Parse(match.Groups[4].Value);
                    LblWins.Text = _wins.ToString();
                    LblLosses.Text = _losses.ToString();
                    LblDraws.Text = _draws.ToString();
                    LblGamesCount.Text = _gamesPlayed.ToString();
                }
            }
            catch { }
        }

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

        private const string TelegramBotUrl = "https://t.me/LichessBotDownoloaderbot";

        private async Task CheckForUpdatesAsync(bool silent)
        {
            string versionUrl = "https://gist.githubusercontent.com/Toliya-max/17c837a5b5a108b5f85b76c3d8dcf9a9/raw/version.txt";

            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("LichessBotGUI/1.0");

                string latestVersion;
                try
                {
                    latestVersion = (await client.GetStringAsync(versionUrl)).Trim().TrimStart('v');
                }
                catch
                {
                    if (!silent) AddLog("Could not reach update server.", LogCategory.Warning);
                    return;
                }

                if (string.IsNullOrEmpty(latestVersion))
                {
                    if (!silent) AddLog("Could not parse version.", LogCategory.Warning);
                    return;
                }

                if (!IsNewerVersion(latestVersion, CurrentVersion))
                {
                    if (!silent) AddLog($"Already up to date (v{CurrentVersion}).", LogCategory.System);
                    return;
                }

                AddLog($"Update available: v{latestVersion} (current: v{CurrentVersion})", LogCategory.Warning);

                var result = MessageBox.Show(
                    $"Update v{latestVersion} is available!\n\nOpen Telegram to download automatically?",
                    "Update Available",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                {
                    // Read license key and pass it via deep link for auto-verification
                    string licenseKey = "";
                    var licCheck = await Task.Run(() => RunLicenseCheck());
                    if (licCheck.Valid && !string.IsNullOrEmpty(licCheck.Key))
                        licenseKey = licCheck.Key.Replace(" ", "");

                    string url = string.IsNullOrEmpty(licenseKey)
                        ? TelegramBotUrl
                        : $"{TelegramBotUrl}?start={licenseKey}";

                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                if (!silent) AddLog($"Update error: {ex.Message}", LogCategory.Error);
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