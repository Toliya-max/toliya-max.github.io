using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LichessBotSetup
{
    public partial class MainWindow : Window
    {
        private readonly string _installDir;
        private readonly HttpClient _http = new HttpClient(new HttpClientHandler()
        {
            SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13
        });

        private const string StockfishUrl = "https://github.com/official-stockfish/Stockfish/releases/download/sf_18/stockfish-windows-x86-64-avx2.zip";

        public MainWindow()
        {
            InitializeComponent();
            _installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LichessBot");

            #pragma warning disable SYSLIB0014
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12 | System.Net.SecurityProtocolType.Tls13;
            #pragma warning restore SYSLIB0014

            string[] args = Environment.GetCommandLineArgs();
            bool isUpdate = args.Length > 1 && args[1].Equals("/update", StringComparison.OrdinalIgnoreCase);

            if (isUpdate)
            {
                // Silent update: read existing token and go straight to install
                string envPath = Path.Combine(_installDir, ".env");
                string token = "";
                if (File.Exists(envPath))
                {
                    foreach (string line in File.ReadAllLines(envPath))
                    {
                        if (line.StartsWith("LICHESS_API_TOKEN="))
                        {
                            token = line.Substring("LICHESS_API_TOKEN=".Length).Trim();
                            break;
                        }
                    }
                }
                TxtToken.Text = token;
                BtnInstall_Click(this, new System.Windows.RoutedEventArgs());
            }
            else if (Directory.Exists(_installDir))
            {
                BtnUninstallOnly.Visibility = Visibility.Visible;
                AlreadyInstalledBanner.Visibility = Visibility.Visible;
            }
        }

        // ════════════════════════════════════════════
        //  WINDOW CHROME
        // ════════════════════════════════════════════
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

        private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            BtnCheckUpdates.IsEnabled = false;
            BtnCheckUpdates.Content = "Checking...";
            try
            {
                const string currentVersion = "1.2.0";
                const string repo = "Toliya-max/lichess-bot";

                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("LichessBotSetup/1.0");

                string latestVersion = "";
                string[] checkUrls = {
                    "https://gist.githubusercontent.com/Toliya-max/17c837a5b5a108b5f85b76c3d8dcf9a9/raw/version.txt",
                };
                foreach (string checkUrl in checkUrls)
                {
                    try { latestVersion = (await client.GetStringAsync(checkUrl)).Trim().TrimStart('v'); if (!string.IsNullOrEmpty(latestVersion)) break; } catch { }
                }

                if (string.IsNullOrEmpty(latestVersion))
                {
                    MessageBox.Show(this, "Could not reach update server. Check your internet connection.", "Update Check Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!Version.TryParse(latestVersion, out var v1) || !Version.TryParse(currentVersion, out var v2) || v1 <= v2)
                {
                    MessageBox.Show(this, $"You already have the latest version (v{currentVersion}).", "No Updates", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var res = MessageBox.Show(this, $"Version v{latestVersion} is available!\n\nOpen the download page?", "Update Available", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (res == MessageBoxResult.Yes)
                    Process.Start(new ProcessStartInfo($"https://github.com/{repo}/releases/latest") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Update check failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnCheckUpdates.IsEnabled = true;
                BtnCheckUpdates.Content = "Check for Updates";
            }
        }

        // ════════════════════════════════════════════
        //  LOGGING
        // ════════════════════════════════════════════
        private void Log(string message)
        {
            Dispatcher.Invoke(() =>
            {
                TxtLog.Text += $"[{DateTime.Now:HH:mm:ss}] {message}\n";
                LogScroll.ScrollToEnd();
            });
        }

        // ════════════════════════════════════════════
        //  STEP INDICATOR UPDATES
        // ════════════════════════════════════════════
        private void SetStepActive(int step)
        {
            Dispatcher.Invoke(() =>
            {
                var accent = Color.FromRgb(0xb5, 0x88, 0x63);
                var accentBorder = Color.FromRgb(0xc9, 0x97, 0x5a);
                var inactive = Color.FromRgb(0x2a, 0x28, 0x26);
                var inactiveBorder = Color.FromRgb(0x3a, 0x38, 0x34);
                var green = Color.FromRgb(0x62, 0x99, 0x24);
                var greenBorder = Color.FromRgb(0x72, 0xa9, 0x34);

                // Step 1
                if (step == 1)
                {
                    Step1Circle.Background = new SolidColorBrush(accent);
                    Step1Circle.BorderBrush = new SolidColorBrush(accentBorder);
                    Step1Text.Text = "1"; Step1Text.Foreground = Brushes.White;
                }
                else
                {
                    Step1Circle.Background = new SolidColorBrush(green);
                    Step1Circle.BorderBrush = new SolidColorBrush(greenBorder);
                    Step1Text.Text = "✓"; Step1Text.Foreground = Brushes.White;
                    Line12.Fill = new SolidColorBrush(green);
                }

                // Step 2
                if (step < 2)
                {
                    Step2Circle.Background = new SolidColorBrush(inactive);
                    Step2Circle.BorderBrush = new SolidColorBrush(inactiveBorder);
                    Step2Text.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
                    Step2Label.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
                }
                else if (step == 2)
                {
                    Step2Circle.Background = new SolidColorBrush(accent);
                    Step2Circle.BorderBrush = new SolidColorBrush(accentBorder);
                    Step2Text.Text = "2"; Step2Text.Foreground = Brushes.White;
                    Step2Label.Foreground = new SolidColorBrush(accent);
                }
                else
                {
                    Step2Circle.Background = new SolidColorBrush(green);
                    Step2Circle.BorderBrush = new SolidColorBrush(greenBorder);
                    Step2Text.Text = "✓"; Step2Text.Foreground = Brushes.White;
                    Step2Label.Foreground = new SolidColorBrush(green);
                    Line23.Fill = new SolidColorBrush(green);
                }

                // Step 3
                if (step < 3)
                {
                    Step3Circle.Background = new SolidColorBrush(inactive);
                    Step3Circle.BorderBrush = new SolidColorBrush(inactiveBorder);
                    Step3Text.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
                    Step3Label.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
                }
                else
                {
                    Step3Circle.Background = new SolidColorBrush(green);
                    Step3Circle.BorderBrush = new SolidColorBrush(greenBorder);
                    Step3Text.Text = "✓"; Step3Text.Foreground = Brushes.White;
                    Step3Label.Foreground = new SolidColorBrush(green);
                }
            });
        }

        // ════════════════════════════════════════════
        //  TASK STATUS UPDATES
        // ════════════════════════════════════════════
        private void SetTaskStatus(TextBlock icon, TextBlock text, string status)
        {
            Dispatcher.Invoke(() =>
            {
                switch (status)
                {
                    case "active":
                        icon.Text = "●";
                        icon.Foreground = new SolidColorBrush(Color.FromRgb(0xb5, 0x88, 0x63));
                        text.Foreground = new SolidColorBrush(Color.FromRgb(0xe8, 0xe6, 0xe3));
                        break;
                    case "done":
                        icon.Text = "✓";
                        icon.Foreground = new SolidColorBrush(Color.FromRgb(0x62, 0x99, 0x24));
                        text.Foreground = new SolidColorBrush(Color.FromRgb(0x7e, 0xaf, 0x85));
                        break;
                    case "skip":
                        icon.Text = "–";
                        icon.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
                        text.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
                        break;
                    case "fail":
                        icon.Text = "✗";
                        icon.Foreground = new SolidColorBrush(Color.FromRgb(0xd3, 0x2f, 0x2f));
                        text.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x80, 0x80));
                        break;
                }
            });
        }

        private void SetProgress(double percent)
        {
            Dispatcher.Invoke(() =>
            {
                double maxWidth = PageInstall.ActualWidth > 60 ? PageInstall.ActualWidth - 60 : 600;
                ProgressFill.Width = maxWidth * (percent / 100.0);
            });
        }

        // ════════════════════════════════════════════
        //  PAGE NAVIGATION
        // ════════════════════════════════════════════
        private void ShowPage(string page)
        {
            Dispatcher.Invoke(() =>
            {
                PageToken.Visibility = page == "token" ? Visibility.Visible : Visibility.Collapsed;
                PageInstall.Visibility = page == "install" ? Visibility.Visible : Visibility.Collapsed;
                PageComplete.Visibility = page == "complete" ? Visibility.Visible : Visibility.Collapsed;
                PageError.Visibility = page == "error" ? Visibility.Visible : Visibility.Collapsed;
            });
        }

        // ════════════════════════════════════════════
        //  KILL OLD PROCESSES
        // ════════════════════════════════════════════
        private void KillOldProcesses()
        {
            string[] processNames = { "LichessBotGUI", "cli", "bot" };
            foreach (string name in processNames)
            {
                try
                {
                    foreach (Process proc in Process.GetProcessesByName(name))
                    {
                        try
                        {
                            proc.Kill();
                            proc.WaitForExit(3000);
                            Log($"Terminated process: {name}.exe");
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }

        // ════════════════════════════════════════════
        //  INSTALL FLOW
        // ════════════════════════════════════════════
        private async void BtnInstall_Click(object sender, RoutedEventArgs e)
        {
            string token = TxtToken.Text.Trim();
            if (string.IsNullOrEmpty(token))
            {
                MessageBox.Show(this, "Please enter your Lichess API Token.", "Missing Token", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Switch to install page
            SetStepActive(2);
            ShowPage("install");
            SetProgress(0);
            TxtLog.Text = "";

            // Kill any running instances before install
            KillOldProcesses();

            try
            {
                // ── Task 1: Validate Token ──
                SetTaskStatus(Task1Icon, Task1Text, "active");
                Log("Checking Lichess Account...");
                _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var accResponse = await _http.GetAsync("https://lichess.org/api/account");
                if (!accResponse.IsSuccessStatusCode)
                    throw new Exception($"Invalid token or API error: {accResponse.StatusCode}");

                string accJson = await accResponse.Content.ReadAsStringAsync();
                string username = "Unknown";
                bool isAlreadyBot = false;

                using (JsonDocument doc = JsonDocument.Parse(accJson))
                {
                    var root = doc.RootElement;
                    username = root.GetProperty("username").GetString() ?? "Unknown";
                    if (root.TryGetProperty("title", out JsonElement titleEl))
                        isAlreadyBot = titleEl.GetString() == "BOT";
                }

                Log($"Authenticated as: {username}");
                SetTaskStatus(Task1Icon, Task1Text, "done");
                SetProgress(15);

                // ── Task 2: Upgrade to BOT ──
                SetTaskStatus(Task2Icon, Task2Text, "active");
                if (!isAlreadyBot)
                {
                    Log("Upgrading account to BOT status...");
                    var upgResponse = await _http.PostAsync("https://lichess.org/api/bot/account/upgrade", null);
                    if (!upgResponse.IsSuccessStatusCode)
                    {
                        string err = await upgResponse.Content.ReadAsStringAsync();
                        throw new Exception($"Failed to upgrade to BOT: {upgResponse.StatusCode} — {err}");
                    }
                    Log("Account upgraded to BOT!");
                    SetTaskStatus(Task2Icon, Task2Text, "done");
                }
                else
                {
                    Log("Account is already a BOT. Skipping.");
                    SetTaskStatus(Task2Icon, Task2Text, "skip");
                    Dispatcher.Invoke(() => Task2Text.Text = "Account already a BOT");
                }
                SetProgress(30);

                // ── Task 3: Extract Payload ──
                SetTaskStatus(Task3Icon, Task3Text, "active");
                Log($"Installing to: {_installDir}");

                if (Directory.Exists(_installDir))
                {
                    Log("Cleaning previous installation...");
                    try { Directory.Delete(_installDir, true); } catch { }
                }
                Directory.CreateDirectory(_installDir);

                Log("Extracting application files...");
                var assembly = Assembly.GetExecutingAssembly();
                using (Stream? stream = assembly.GetManifestResourceStream("LichessBotSetup.Payload.zip"))
                {
                    if (stream == null) throw new Exception("Payload.zip not found in installer resources.");
                    using (ZipArchive archive = new ZipArchive(stream))
                    {
                        archive.ExtractToDirectory(_installDir, overwriteFiles: true);
                    }
                }
                Log("Files extracted.");
                SetTaskStatus(Task3Icon, Task3Text, "done");
                SetProgress(50);

                // ── Task 4: Download Chess Engine ──
                SetTaskStatus(Task4Icon, Task4Text, "active");
                await DownloadStockfishEngine();
                SetTaskStatus(Task4Icon, Task4Text, "done");
                SetProgress(70);

                // ── Task 5: Python & Dependencies ──
                SetTaskStatus(Task5Icon, Task5Text, "active");
                Log("Checking for Python...");
                if (!await IsPythonInstalled())
                {
                    Log("Python not found. Downloading installer...");
                    await DownloadAndInstallPython();
                    Environment.SetEnvironmentVariable("PATH",
                        Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User)
                        + ";" + Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine));
                }
                else
                {
                    Log("Python found.");
                }

                Log("Installing Python packages...");
                await InstallPipRequirementsAsync(_installDir);
                SetTaskStatus(Task5Icon, Task5Text, "done");
                SetProgress(90);

                // ── Task 6: Write .env & Create Shortcut ──
                SetTaskStatus(Task6Icon, Task6Text, "active");
                string envPath = Path.Combine(_installDir, ".env");
                File.WriteAllText(envPath, $"LICHESS_API_TOKEN={token}\n");
                Log("API token saved.");

                CreateShortcut();
                Log("Desktop shortcut created.");
                RegisterInWindowsApps();
                SetTaskStatus(Task6Icon, Task6Text, "done");
                SetProgress(100);

                // ── DONE ──
                Log("Installation complete!");
                await Task.Delay(600);

                SetStepActive(3);
                Dispatcher.Invoke(() => TxtInstallPath.Text = _installDir);
                ShowPage("complete");
            }
            catch (Exception ex)
            {
                string errMsg = ex.Message;
                if (ex.InnerException != null) errMsg += "\n" + ex.InnerException.Message;
                Log($"ERROR: {errMsg}");

                SetStepActive(2);
                Dispatcher.Invoke(() => TxtErrorDetail.Text = errMsg);
                ShowPage("error");
            }
        }

        // ════════════════════════════════════════════
        //  STOCKFISH ENGINE DOWNLOAD
        // ════════════════════════════════════════════
        private async Task DownloadStockfishEngine()
        {
            string engineDir = Path.Combine(_installDir, "stockfish18");

            // Check if engine already exists in the extracted payload
            if (Directory.Exists(engineDir))
            {
                var exeFiles = Directory.GetFiles(engineDir, "*.exe", SearchOption.AllDirectories);
                if (exeFiles.Length > 0)
                {
                    Log($"Chess engine found in package. Skipping download.");
                    return;
                }
            }

            // Also check for any stockfish exe in the install dir
            string[] possibleEngines = Directory.GetFiles(_installDir, "stockfish*.exe", SearchOption.AllDirectories);
            if (possibleEngines.Length > 0)
            {
                Log($"Chess engine found: {Path.GetFileName(possibleEngines[0])}");
                return;
            }

            Log("Downloading Stockfish 18...");
            string zipPath = Path.Combine(Path.GetTempPath(), "stockfish_setup.zip");
            string tempExtract = Path.Combine(Path.GetTempPath(), "stockfish_setup_temp");

            try
            {
                using (var response = await _http.GetAsync(StockfishUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    using (var stream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await stream.CopyToAsync(fileStream);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Direct download failed ({ex.Message}). Trying PowerShell fallback...");
                var tcs = new TaskCompletionSource<bool>();
                Process ps = new Process();
                ps.StartInfo.FileName = "powershell.exe";
                ps.StartInfo.Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri '{StockfishUrl}' -OutFile '{zipPath}'\"";
                ps.StartInfo.UseShellExecute = false;
                ps.StartInfo.CreateNoWindow = true;
                ps.EnableRaisingEvents = true;
                ps.Exited += (s, e) => tcs.SetResult(ps.ExitCode == 0);
                ps.Start();

                bool ok = await tcs.Task;
                if (!ok || !File.Exists(zipPath))
                    throw new Exception("Failed to download Stockfish engine. Check your internet connection.");
                Log("Fallback download successful.");
            }

            Log("Extracting engine...");
            if (Directory.Exists(tempExtract)) Directory.Delete(tempExtract, true);
            ZipFile.ExtractToDirectory(zipPath, tempExtract);

            // Move extracted content to target
            Directory.CreateDirectory(engineDir);
            var dirs = Directory.GetDirectories(tempExtract);
            if (dirs.Length > 0)
            {
                // Move contents from the inner folder to engineDir
                foreach (var file in Directory.GetFiles(dirs[0], "*", SearchOption.AllDirectories))
                {
                    string relativePath = Path.GetRelativePath(dirs[0], file);
                    string destPath = Path.Combine(engineDir, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    File.Copy(file, destPath, true);
                }
            }

            // Cleanup
            try { File.Delete(zipPath); } catch { }
            try { Directory.Delete(tempExtract, true); } catch { }

            Log("Stockfish engine installed!");
        }

        // ════════════════════════════════════════════
        //  PYTHON CHECK / INSTALL
        // ════════════════════════════════════════════
        private async Task<bool> IsPythonInstalled()
        {
            try
            {
                Process p = new Process();
                p.StartInfo.FileName = "python";
                p.StartInfo.Arguments = "--version";
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.RedirectStandardOutput = true;
                p.Start();
                string output = await p.StandardOutput.ReadToEndAsync();
                await p.WaitForExitAsync();
                if (p.ExitCode == 0)
                {
                    Log($"Found: {output.Trim()}");
                    return true;
                }
                return false;
            }
            catch { return false; }
        }

        private async Task DownloadAndInstallPython()
        {
            string url = "https://www.python.org/ftp/python/3.12.3/python-3.12.3-amd64.exe";
            string installerPath = Path.Combine(Path.GetTempPath(), "python_installer.exe");

            Log("Downloading Python 3.12...");
            try
            {
                using (var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    using (var stream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await stream.CopyToAsync(fileStream);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Direct download failed ({ex.Message}). Trying PowerShell...");
                var psTcs = new TaskCompletionSource<bool>();
                Process ps = new Process();
                ps.StartInfo.FileName = "powershell.exe";
                ps.StartInfo.Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri '{url}' -OutFile '{installerPath}'\"";
                ps.StartInfo.UseShellExecute = false;
                ps.StartInfo.CreateNoWindow = true;
                ps.EnableRaisingEvents = true;
                ps.Exited += (s, e) => psTcs.SetResult(ps.ExitCode == 0);
                ps.Start();

                bool ok = await psTcs.Task;
                if (!ok || !File.Exists(installerPath))
                    throw new Exception("Failed to download Python. Please install Python 3.12 manually from python.org.");
                Log("Fallback download successful.");
            }

            Log("Installing Python silently (this may take a few minutes)...");
            var tcs = new TaskCompletionSource<bool>();
            Process p = new Process();
            p.StartInfo.FileName = installerPath;
            p.StartInfo.Arguments = "/quiet InstallAllUsers=0 PrependPath=1 Include_test=0";
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.CreateNoWindow = true;
            p.EnableRaisingEvents = true;
            p.Exited += (s, e) => tcs.SetResult(p.ExitCode == 0);
            p.Start();

            bool success = await tcs.Task;
            if (!success)
                throw new Exception("Python installation failed. Please install Python 3.12 manually.");
            Log("Python installed!");
        }

        private Task InstallPipRequirementsAsync(string installDir)
        {
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();

            Process p = new Process();
            p.StartInfo.FileName = "python";
            p.StartInfo.Arguments = "-m pip install -r requirements.txt";
            p.StartInfo.WorkingDirectory = installDir;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.RedirectStandardError = true;

            p.OutputDataReceived += (s, e) => { if (e.Data != null) Log($"[pip] {e.Data}"); };
            p.ErrorDataReceived += (s, e) => { if (e.Data != null) Log($"[pip] {e.Data}"); };
            p.EnableRaisingEvents = true;
            p.Exited += (s, e) =>
            {
                if (p.ExitCode != 0)
                    Log($"[pip] Warning: exited with code {p.ExitCode}");
                tcs.SetResult(true);
            };

            try
            {
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                Log($"Failed to run pip: {ex.Message}");
                throw new Exception("Python/pip not found. Please restart your computer or install Python manually.", ex);
            }

            return tcs.Task;
        }

        // ════════════════════════════════════════════
        //  WINDOWS APPS & FEATURES REGISTRY
        // ════════════════════════════════════════════
        private void RegisterInWindowsApps()
        {
            try
            {
                string exePath = Path.Combine(_installDir, "LichessBotGUI", "LichessBotGUI.exe");
                string setupPath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                long sizeKB = 0;
                try { sizeKB = new System.IO.DirectoryInfo(_installDir).GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) / 1024; } catch { }

                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\LichessBot");
                key.SetValue("DisplayName", "Lichess Bot");
                key.SetValue("DisplayVersion", "1.0.0");
                key.SetValue("Publisher", "Toliya-max");
                key.SetValue("InstallLocation", _installDir);
                key.SetValue("DisplayIcon", $"\"{exePath}\"");
                key.SetValue("UninstallString", $"\"{setupPath}\" /uninstall");
                key.SetValue("NoModify", 1, Microsoft.Win32.RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, Microsoft.Win32.RegistryValueKind.DWord);
                key.SetValue("EstimatedSize", (int)Math.Min(sizeKB, int.MaxValue), Microsoft.Win32.RegistryValueKind.DWord);
                Log("Registered in Windows Apps & Features.");
            }
            catch (Exception ex)
            {
                Log($"Warning: Could not register in Windows Apps: {ex.Message}");
            }
        }

        // ════════════════════════════════════════════
        //  SHORTCUT
        // ════════════════════════════════════════════
        private void CreateShortcut()
        {
            string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string shortcutPath = Path.Combine(desktopDir, "Lichess Bot.lnk");
            string targetPath = Path.Combine(_installDir, "LichessBotGUI", "LichessBotGUI.exe");

            try
            {
                string psScript = $@"
$WshShell = New-Object -comObject WScript.Shell
$Shortcut = $WshShell.CreateShortcut('{shortcutPath}')
$Shortcut.TargetPath = '{targetPath}'
$Shortcut.WorkingDirectory = '{_installDir}'
$Shortcut.Save()
";
                ProcessStartInfo psi = new ProcessStartInfo()
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{psScript}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                Process.Start(psi)?.WaitForExit();
            }
            catch (Exception ex)
            {
                Log($"Warning: Could not create shortcut: {ex.Message}");
            }
        }

        // ════════════════════════════════════════════
        //  POST-INSTALL ACTIONS
        // ════════════════════════════════════════════
        private void BtnLaunch_Click(object sender, RoutedEventArgs e)
        {
            string targetPath = Path.Combine(_installDir, "LichessBotGUI", "LichessBotGUI.exe");
            if (File.Exists(targetPath))
            {
                Process.Start(new ProcessStartInfo()
                {
                    FileName = targetPath,
                    WorkingDirectory = Path.Combine(_installDir, "LichessBotGUI"),
                    UseShellExecute = true
                });
                Application.Current.Shutdown();
            }
            else
            {
                MessageBox.Show(this, "Executable not found. Installation may have failed.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnUninstall_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(this,
                $"This will permanently delete all Lichess Bot files from:\n\n{_installDir}\n\nAnd the Desktop shortcut.\n\nAre you sure?",
                "Confirm Uninstall", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                if (Directory.Exists(_installDir))
                    Directory.Delete(_installDir, recursive: true);

                string shortcutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Lichess Bot.lnk");
                if (File.Exists(shortcutPath))
                    File.Delete(shortcutPath);

                try
                {
                    Microsoft.Win32.Registry.CurrentUser.DeleteSubKey(
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\LichessBot", false);
                }
                catch { }

                MessageBox.Show(this, "Lichess Bot has been successfully uninstalled!", "Uninstall Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Uninstall failed:\n\n{ex.Message}\n\nTry manually deleting: {_installDir}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnRetry_Click(object sender, RoutedEventArgs e)
        {
            SetStepActive(1);
            ShowPage("token");
        }
    }
}
