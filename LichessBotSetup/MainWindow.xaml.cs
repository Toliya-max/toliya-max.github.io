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
        private bool _isUpdateMode = false;
        private readonly HttpClient _http = new HttpClient(new HttpClientHandler()
        {
            SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13
        });

        private const string StockfishUrl = "https://github.com/official-stockfish/Stockfish/releases/download/sf_18/stockfish-windows-x86-64-avx2.zip";

        private string SecretsCacheDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LichessBot-cache", "secrets");

        private static string? ReadTokenFromEnv(string envPath)
        {
            if (!File.Exists(envPath)) return null;
            foreach (string line in File.ReadAllLines(envPath))
            {
                if (line.StartsWith("LICHESS_API_TOKEN="))
                    return line.Substring("LICHESS_API_TOKEN=".Length).Trim();
            }
            return null;
        }

        private (string? token, bool hasLicense) LoadCachedCredentials()
        {
            string cachedEnv = Path.Combine(SecretsCacheDir, ".env");
            string cachedLic = Path.Combine(SecretsCacheDir, "license.dat");
            string installEnv = Path.Combine(_installDir, ".env");
            string installLic = Path.Combine(_installDir, "license.dat");

            string? token = ReadTokenFromEnv(installEnv) ?? ReadTokenFromEnv(cachedEnv);
            bool hasLicense = File.Exists(installLic) || File.Exists(cachedLic);
            return (token, hasLicense);
        }

        private void SaveCachedCredentials()
        {
            try
            {
                Directory.CreateDirectory(SecretsCacheDir);
                string installEnv = Path.Combine(_installDir, ".env");
                string installLic = Path.Combine(_installDir, "license.dat");
                if (File.Exists(installEnv))
                    File.Copy(installEnv, Path.Combine(SecretsCacheDir, ".env"), overwrite: true);
                if (File.Exists(installLic))
                    File.Copy(installLic, Path.Combine(SecretsCacheDir, "license.dat"), overwrite: true);
            }
            catch (Exception ex)
            {
                Log($"[CACHE] Could not save credentials: {ex.Message}");
            }
        }

        private bool RestoreCachedCredentialsIfMissing()
        {
            try
            {
                string cachedEnv = Path.Combine(SecretsCacheDir, ".env");
                string cachedLic = Path.Combine(SecretsCacheDir, "license.dat");
                string installEnv = Path.Combine(_installDir, ".env");
                string installLic = Path.Combine(_installDir, "license.dat");

                bool restored = false;
                if (!File.Exists(installEnv) && File.Exists(cachedEnv))
                {
                    Directory.CreateDirectory(_installDir);
                    File.Copy(cachedEnv, installEnv, overwrite: false);
                    restored = true;
                }
                if (!File.Exists(installLic) && File.Exists(cachedLic))
                {
                    Directory.CreateDirectory(_installDir);
                    File.Copy(cachedLic, installLic, overwrite: false);
                    restored = true;
                }
                return restored;
            }
            catch (Exception ex)
            {
                Log($"[CACHE] Restore failed: {ex.Message}");
                return false;
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            _installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LichessBot");

            try
            {
                string? v = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3);
                if (!string.IsNullOrEmpty(v) && TitleVersion != null)
                {
                    TitleVersion.Text = $"Lichess Bot Setup v{v}";
                    this.Title = $"Lichess Bot Setup v{v}";
                }
            }
            catch { }

            #pragma warning disable SYSLIB0014
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12 | System.Net.SecurityProtocolType.Tls13;
            #pragma warning restore SYSLIB0014

            string[] args = Environment.GetCommandLineArgs();
            bool isUpdate = args.Length > 1 && args[1].Equals("/update", StringComparison.OrdinalIgnoreCase);

            var (cachedToken, cachedHasLicense) = LoadCachedCredentials();

            if (isUpdate)
            {
                _isUpdateMode = true;
                TxtToken.Text = cachedToken ?? "";
                BtnInstall_Click(this, new System.Windows.RoutedEventArgs());
                return;
            }

            bool alreadyInstalled = Directory.Exists(_installDir);
            if (alreadyInstalled)
            {
                BtnUninstallOnly.Visibility = Visibility.Visible;
                AlreadyInstalledBanner.Visibility = Visibility.Visible;
            }

            if (!string.IsNullOrEmpty(cachedToken) && cachedHasLicense)
            {
                _isUpdateMode = true;
                TxtToken.Text = cachedToken;
                Loaded += (s, e) => BtnInstall_Click(this, new System.Windows.RoutedEventArgs());
                return;
            }

            if (alreadyInstalled && !string.IsNullOrEmpty(cachedToken))
            {
                TxtToken.Text = cachedToken;
            }
        }

        // ════════════════════════════════════════════
        //  WINDOW CHROME
        // ════════════════════════════════════════════
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

        // ════════════════════════════════════════════
        //  SETUP SUB-PAGE NAVIGATION
        // ════════════════════════════════════════════
        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            string key = TxtLicenseKey?.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(key))
            {
                ShowLicenseStatus("Please enter your license key.", isError: true);
                return;
            }
            var result = ValidateLicenseKey(key);
            if (!result.Valid)
            {
                ShowLicenseStatus(result.Error ?? "Invalid license key.", isError: true);
                return;
            }
            ShowLicenseStatus($"Valid: {result.Info}", isError: false);

            SubPageLicense.Visibility = Visibility.Collapsed;
            SubPageToken.Visibility = Visibility.Visible;
            BtnCheckUpdates.Visibility = Visibility.Collapsed;
            BtnBack.Visibility = Visibility.Visible;
            BtnNext.Visibility = Visibility.Collapsed;
            BtnInstall.Visibility = Visibility.Visible;
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            SubPageToken.Visibility = Visibility.Collapsed;
            SubPageLicense.Visibility = Visibility.Visible;
            BtnBack.Visibility = Visibility.Collapsed;
            BtnCheckUpdates.Visibility = Visibility.Visible;
            BtnNext.Visibility = Visibility.Visible;
            BtnInstall.Visibility = Visibility.Collapsed;
        }

        // ════════════════════════════════════════════
        //  LICENSE VALIDATION
        // ════════════════════════════════════════════
        private record LicenseValidationResult(bool Valid, string? Info, string? Error);

        // Obfuscated secrets — must match license.py exactly
        private static readonly byte[] _mask = [
            0x5a, 0x3f, 0x7c, 0x11, 0x88, 0xd2, 0x44, 0xab,
            0x9e, 0x61, 0x23, 0x57, 0xf0, 0x04, 0xbc, 0x77,
            0x31, 0xca, 0x09, 0x5b, 0xe8, 0x6f, 0xd3, 0x18,
            0xa4, 0x72, 0xb6, 0x4d, 0x0c, 0x93, 0x2e, 0xf5,
        ];
        private static readonly byte[] _hsEnc = [
             58,   3, 244, 136, 208,  32, 225, 123,
            254,  23, 180, 147,   6,  36,  28, 109,
             95, 164, 201,  34,  77,  41,  59, 155,
             69,  59, 253, 148, 252, 221,  55, 234,
        ];
        private static byte[] GetHmacSecret()
        {
            var r = new byte[_hsEnc.Length];
            for (int i = 0; i < r.Length; i++) r[i] = (byte)(_hsEnc[i] ^ _mask[i % _mask.Length]);
            return r;
        }

        private LicenseValidationResult ValidateLicenseKey(string key)
        {
            // Key format v2: version(1)+type(1)+expiry(4)+nonce(4)+HMAC[:14] = 24 bytes → 40 base32 chars
            // Must mirror license.py exactly.
            try
            {
                string cleaned = key.Trim().ToUpperInvariant().Replace("-", "").Replace(" ", "");
                if (cleaned.Length != 40)
                    return new LicenseValidationResult(false, null, "Invalid key length.");

                // Base32 decode
                string padded = cleaned + new string('=', (8 - cleaned.Length % 8) % 8);
                byte[] raw;
                try { raw = Base32Decode(padded); }
                catch { return new LicenseValidationResult(false, null, "Invalid key encoding."); }

                if (raw.Length < 24)
                    return new LicenseValidationResult(false, null, "Key too short.");

                // v2 layout: [0]=version [1]=type [2:6]=expiry [6:10]=nonce [10:24]=sig
                if (raw[0] != 0x02)
                    return new LicenseValidationResult(false, null, "Unsupported key version.");

                byte keyType = raw[1];
                uint expiryTs = (uint)((raw[2] << 24) | (raw[3] << 16) | (raw[4] << 8) | raw[5]);
                byte[] storedSig = new byte[14];
                Array.Copy(raw, 10, storedSig, 0, 14);

                // header = raw[0..10]
                byte[] header = new byte[10];
                Array.Copy(raw, 0, header, 0, 10);

                byte[] fullHmac;
                using (var hmac = new System.Security.Cryptography.HMACSHA256(GetHmacSecret()))
                    fullHmac = hmac.ComputeHash(header);

                byte[] expectedSig = new byte[14];
                Array.Copy(fullHmac, 0, expectedSig, 0, 14);

                if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(storedSig, expectedSig))
                    return new LicenseValidationResult(false, null, "Key signature invalid.");

                byte[] validTypes = { 0x31, 0x57, 0x51, 0x4D, 0x59, 0x44 };
                if (!validTypes.Contains(keyType))
                    return new LicenseValidationResult(false, null, "Unknown key type.");

                bool isDev = keyType == 0x44;
                var expiry = DateTimeOffset.FromUnixTimeSeconds(expiryTs).UtcDateTime;
                if (!isDev && expiry < DateTime.UtcNow)
                    return new LicenseValidationResult(false, null, $"License expired on {expiry:yyyy-MM-dd}. Please renew.");

                string planName = keyType == 0x4D ? "Monthly" : (keyType == 0x59 ? "Yearly" : "Developer");
                string info = isDev
                    ? $"{planName} — no expiry"
                    : $"{planName} — expires {expiry:yyyy-MM-dd} ({(int)(expiry - DateTime.UtcNow).TotalDays} days)";
                return new LicenseValidationResult(true, info, null);
            }
            catch (Exception ex)
            {
                return new LicenseValidationResult(false, null, $"Validation error: {ex.Message}");
            }
        }

        private static byte[] Base32Decode(string input)
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            string s = input.TrimEnd('=');
            int outputLen = s.Length * 5 / 8;
            byte[] result = new byte[outputLen];
            int buffer = 0, bitsLeft = 0, idx = 0;
            foreach (char c in s)
            {
                int val = alphabet.IndexOf(c);
                if (val < 0) throw new FormatException($"Invalid base32 char: {c}");
                buffer = (buffer << 5) | val;
                bitsLeft += 5;
                if (bitsLeft >= 8)
                {
                    bitsLeft -= 8;
                    result[idx++] = (byte)(buffer >> bitsLeft);
                }
            }
            return result;
        }

        private void ShowLicenseStatus(string message, bool isError)
        {
            Dispatcher.Invoke(() =>
            {
                if (LicenseStatusBorder == null || TxtLicenseStatus == null) return;
                LicenseStatusBorder.Visibility = Visibility.Visible;
                TxtLicenseStatus.Text = message;
                if (isError)
                {
                    TxtLicenseStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xf8, 0x51, 0x49));
                    LicenseStatusBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xc9, 0x37, 0x2c));
                    LicenseStatusBorder.Background = new SolidColorBrush(Color.FromArgb(0x18, 0xc9, 0x37, 0x2c));
                }
                else
                {
                    TxtLicenseStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x3f, 0xb9, 0x50));
                    LicenseStatusBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3f, 0xb9, 0x50));
                    LicenseStatusBorder.Background = new SolidColorBrush(Color.FromArgb(0x18, 0x3f, 0xb9, 0x50));
                }
            });
        }

        private const string UpdateBotUrl = "https://t.me/LichessBotDownoloaderbot";

        private void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var res = MessageBox.Show(this,
                    "Updates are delivered via the Telegram distribution bot.\n\n" +
                    "Open it now? The bot will send you the latest Lichess Bot Setup ZIP " +
                    "directly in chat - just tap the file, extract it, and run setup.",
                    "Check for Updates", MessageBoxButton.YesNo, MessageBoxImage.Information);

                if (res == MessageBoxResult.Yes)
                {
                    Process.Start(new ProcessStartInfo(UpdateBotUrl) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"Could not open the Telegram bot: {ex.Message}\n\nURL: {UpdateBotUrl}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
        private static readonly string[] BotProcessNames = {
            "LichessBotGUI",
            "pythonw", "python", "py",
            "stockfish", "stockfish-windows-x86-64-avx2", "stockfish_18",
            "cli", "bot",
        };

        private static readonly string[] BotProcessNamesOwnedOnly = {
            "pythonw", "python", "py",
            "stockfish", "stockfish-windows-x86-64-avx2", "stockfish_18",
            "cli", "bot",
        };

        private bool _processHoldsPath(Process p, string root)
        {
            try
            {
                string? file = p.MainModule?.FileName;
                if (string.IsNullOrEmpty(file)) return false;
                return file.StartsWith(root, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private void KillBotProcesses(bool ownedOnly = false)
        {
            var names = ownedOnly ? BotProcessNamesOwnedOnly : BotProcessNames;
            foreach (string name in names)
            {
                try
                {
                    foreach (Process proc in Process.GetProcessesByName(name))
                    {
                        try
                        {
                            if (ownedOnly && !_processHoldsPath(proc, _installDir))
                                continue;
                            proc.Kill(entireProcessTree: true);
                            proc.WaitForExit(3000);
                            Log($"Terminated: {name}.exe (pid {proc.Id})");
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }

        private void KillOldProcesses() => KillBotProcesses();

        private async Task<bool> ForceDeleteDirectoryAsync(string path, int maxAttempts = 6)
        {
            if (!Directory.Exists(path)) return true;

            try
            {
                foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
                }
            }
            catch { }

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    Directory.Delete(path, recursive: true);
                    return true;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    Log($"Delete attempt {attempt}/{maxAttempts} failed: {ex.Message}");

                    int killedRm = 0;
                    try { killedRm = RestartManager.KillLockers(path, Log); } catch { }
                    if (killedRm > 0) Log($"Restart Manager terminated {killedRm} locking process(es)");

                    KillBotProcesses(ownedOnly: attempt <= 2);
                    await Task.Delay(400 * attempt);
                }
            }

            try
            {
                string bak = path + "_old_" + DateTime.Now.Ticks;
                Directory.Move(path, bak);
                Log($"Could not delete, moved aside: {bak}");
                try { Directory.Delete(bak, recursive: true); } catch { }
                return true;
            }
            catch (Exception ex)
            {
                Log($"ForceDelete failed completely: {ex.Message}");
                return false;
            }
        }

        // ════════════════════════════════════════════
        //  INSTALL FLOW
        // ════════════════════════════════════════════
        private async void BtnInstall_Click(object sender, RoutedEventArgs e)
        {
            // ── License Key Check (skipped on update — key already validated at first install) ──
            if (!_isUpdateMode)
            {
                string licenseKey = TxtLicenseKey?.Text?.Trim() ?? "";
                if (string.IsNullOrEmpty(licenseKey))
                {
                    MessageBox.Show(this, "You have not entered a license key.\nPlease enter your license key first.",
                        "License Key Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    BtnBack_Click(sender, e);
                    return;
                }

                ShowLicenseStatus("Validating license key...", isError: false);
                var licResult = await Task.Run(() => ValidateLicenseKey(licenseKey));
                if (!licResult.Valid)
                {
                    MessageBox.Show(this, licResult.Error ?? "Invalid license key.",
                        "Invalid License", MessageBoxButton.OK, MessageBoxImage.Warning);
                    BtnBack_Click(sender, e);
                    return;
                }
                ShowLicenseStatus($"License valid: {licResult.Info}", isError: false);
            }

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

            var sw = System.Diagnostics.Stopwatch.StartNew();
            long lastMark = 0;
            void Mark(string label)
            {
                long now = sw.ElapsedMilliseconds;
                Log($"[+{now - lastMark} ms] {label} (total {now} ms)");
                lastMark = now;
            }

            // Kill any running instances before install
            KillOldProcesses();
            Mark("kill old processes");

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
                Mark("lichess token check");
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

                if (RestoreCachedCredentialsIfMissing())
                    Log("Credentials restored from secrets cache");

                byte[]? savedLicDat = null;
                byte[]? savedEnv = null;
                string licDatPath = Path.Combine(_installDir, "license.dat");
                string oldEnvPath = Path.Combine(_installDir, ".env");
                if (File.Exists(licDatPath))
                    savedLicDat = File.ReadAllBytes(licDatPath);
                if (File.Exists(oldEnvPath))
                    savedEnv = File.ReadAllBytes(oldEnvPath);

                Log("Stopping running processes...");
                KillBotProcesses();
                await Task.Delay(500);

                string? stockfishBak = null;
                string? venvBak = null;
                string? settingsBak = null;
                string sfSrc = Path.Combine(_installDir, "stockfish18");
                string venvSrc = Path.Combine(_installDir, "venv");
                string settingsSrc = Path.Combine(_installDir, "settings.json");

                if (Directory.Exists(_installDir))
                {
                    if (Directory.Exists(sfSrc))
                    {
                        stockfishBak = Path.Combine(Path.GetTempPath(),
                            "lichess_sf_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                        try { Directory.Move(sfSrc, stockfishBak); Log("Reusing Stockfish engine from previous install"); }
                        catch (Exception ex) { Log($"Stockfish backup skipped: {ex.Message}"); stockfishBak = null; }
                    }
                    if (Directory.Exists(venvSrc))
                    {
                        venvBak = Path.Combine(Path.GetTempPath(),
                            "lichess_venv_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                        try { Directory.Move(venvSrc, venvBak); Log("Reusing venv from previous install"); }
                        catch (Exception ex) { Log($"venv backup skipped: {ex.Message}"); venvBak = null; }
                    }
                    if (File.Exists(settingsSrc))
                    {
                        settingsBak = settingsSrc + ".bak";
                        try { File.Copy(settingsSrc, settingsBak, overwrite: true); }
                        catch { settingsBak = null; }
                    }

                    Log("Cleaning previous installation...");
                    if (!await ForceDeleteDirectoryAsync(_installDir))
                        throw new Exception(
                            $"Could not clean {_installDir}. " +
                            "Close all Lichess Bot processes in Task Manager or reboot, then try again.");
                }
                Directory.CreateDirectory(_installDir);

                if (stockfishBak != null && Directory.Exists(stockfishBak))
                {
                    try { Directory.Move(stockfishBak, sfSrc); }
                    catch (Exception ex) { Log($"Could not restore Stockfish: {ex.Message}"); }
                }
                if (venvBak != null && Directory.Exists(venvBak))
                {
                    try { Directory.Move(venvBak, venvSrc); }
                    catch (Exception ex) { Log($"Could not restore venv: {ex.Message}"); }
                }
                if (settingsBak != null && File.Exists(settingsBak))
                {
                    try { File.Move(settingsBak, settingsSrc, overwrite: true); }
                    catch { }
                }

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
                Mark("payload extract + restore");
                SetTaskStatus(Task3Icon, Task3Text, "done");
                SetProgress(50);

                // ── Task 4: skipped - engine downloads on first bot launch ──
                SetTaskStatus(Task4Icon, Task4Text, "skip");
                Dispatcher.Invoke(() => Task4Text.Text = "Engine: downloaded on first launch");
                SetProgress(55);

                // ── Task 5: Python ──
                SetTaskStatus(Task5Icon, Task5Text, "active");

                var pythonTask = Task.Run(async () =>
                {
                    var t = System.Diagnostics.Stopwatch.StartNew();
                    Dispatcher.Invoke(() => Log("Checking for Python..."));
                    if (!await IsPythonInstalled())
                    {
                        Dispatcher.Invoke(() => Log("Python not found. Downloading installer..."));
                        await DownloadAndInstallPython();
                        Environment.SetEnvironmentVariable("PATH",
                            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User)
                            + ";" + Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine));
                    }
                    else
                    {
                        Dispatcher.Invoke(() => Log("Python found."));
                    }
                    long tPython = t.ElapsedMilliseconds;
                    if (await ArePipRequirementsSatisfiedAsync(_installDir))
                    {
                        Dispatcher.Invoke(() => Log("Python packages already satisfied - skipping pip"));
                    }
                    else
                    {
                        Dispatcher.Invoke(() => Log("Installing Python packages..."));
                        await InstallPipRequirementsAsync(_installDir);
                    }
                    Dispatcher.Invoke(() =>
                    {
                        Log($"[timing] python+pip task: {t.ElapsedMilliseconds} ms (python: {tPython} ms, pip: {t.ElapsedMilliseconds - tPython} ms)");
                        SetTaskStatus(Task5Icon, Task5Text, "done");
                        SetProgress(85);
                    });
                });

                await pythonTask;
                Mark("python+pip");
                SetProgress(90);

                // ── Task 6: Write .env & Create Shortcut ──
                SetTaskStatus(Task6Icon, Task6Text, "active");
                string envPath = Path.Combine(_installDir, ".env");
                if (savedEnv != null)
                {
                    // Restore saved .env (update — keep existing token)
                    File.WriteAllBytes(envPath, savedEnv);
                    Log("API token restored from previous installation.");
                }
                else if (!string.IsNullOrEmpty(token))
                {
                    File.WriteAllText(envPath, $"LICHESS_API_TOKEN={token}\n");
                    Log("API token saved.");
                }

                // Restore license.dat if it existed before the update
                if (savedLicDat != null)
                {
                    File.WriteAllBytes(Path.Combine(_installDir, "license.dat"), savedLicDat);
                    Log("License restored from previous installation.");
                }

                string licenseKeyToSave = TxtLicenseKey?.Text?.Trim() ?? "";
                if (!_isUpdateMode && savedLicDat == null && !string.IsNullOrEmpty(licenseKeyToSave))
                {
                    bool saved = await SaveLicenseAsync(licenseKeyToSave);
                    Log(saved
                        ? "License key saved to license.dat"
                        : "WARNING: could not save license.dat, user will be asked to activate on first launch");
                }
                else if (savedLicDat != null)
                {
                    Log("Existing license.dat restored (already done above)");
                }

                AutoConfigureEngine();
                CreateShortcut();
                Log("Desktop shortcut created.");
                RegisterInWindowsApps();
                SaveCachedCredentials();
                SetTaskStatus(Task6Icon, Task6Text, "done");
                SetProgress(100);

                Mark("shortcut + registry + .env");

                // ── DONE ──
                Log($"Installation complete in {sw.ElapsedMilliseconds} ms!");
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
        private string EngineCacheDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LichessBot-cache", "stockfish");

        private async Task DownloadStockfishEngine()
        {
            string engineDir = Path.Combine(_installDir, "stockfish18");

            if (Directory.Exists(engineDir))
            {
                var exeFiles = Directory.GetFiles(engineDir, "*.exe", SearchOption.AllDirectories);
                if (exeFiles.Length > 0)
                {
                    Log($"Chess engine found in install dir. Skipping.");
                    return;
                }
            }

            string[] possibleEngines = Directory.GetFiles(_installDir, "stockfish*.exe", SearchOption.AllDirectories);
            if (possibleEngines.Length > 0)
            {
                Log($"Chess engine found: {Path.GetFileName(possibleEngines[0])}");
                return;
            }

            if (Directory.Exists(EngineCacheDir))
            {
                var cachedExes = Directory.GetFiles(EngineCacheDir, "stockfish*.exe");
                if (cachedExes.Length > 0)
                {
                    Directory.CreateDirectory(engineDir);
                    foreach (var src in cachedExes)
                    {
                        string dst = Path.Combine(engineDir, Path.GetFileName(src));
                        File.Copy(src, dst, overwrite: true);
                    }
                    Log($"Chess engine restored from cache ({EngineCacheDir})");
                    return;
                }
            }

            Log("Downloading Stockfish 18 (one-time, will be cached)...");
            string zipPath = Path.Combine(Path.GetTempPath(), "stockfish_setup.zip");
            string tempExtract = Path.Combine(Path.GetTempPath(), "stockfish_setup_temp");

            try
            {
                using var response = await _http.GetAsync(StockfishUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                long totalBytes = response.Content.Headers.ContentLength ?? -1;
                using var stream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920);
                byte[] buffer = new byte[81920];
                long downloaded = 0;
                int read;
                while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, read);
                    downloaded += read;
                    if (totalBytes > 0)
                    {
                        int pct = (int)(downloaded * 100 / totalBytes);
                        Dispatcher.Invoke(() => Log($"Downloading engine... {pct}% ({downloaded / 1024 / 1024}MB / {totalBytes / 1024 / 1024}MB)"));
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Direct download failed ({ex.Message}). Trying PowerShell...");
                var tcs = new TaskCompletionSource<bool>();
                Process ps = new Process();
                ps.StartInfo.FileName = "powershell.exe";
                ps.StartInfo.Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri '{StockfishUrl}' -OutFile '{zipPath}'\"";
                ps.StartInfo.UseShellExecute = false;
                ps.StartInfo.CreateNoWindow = true;
                ps.EnableRaisingEvents = true;
                ps.Exited += (s, e) => tcs.SetResult(ps.ExitCode == 0);
                ps.Start();
                if (!await tcs.Task || !File.Exists(zipPath))
                    throw new Exception("Failed to download Stockfish engine.");
            }

            Log("Extracting engine...");
            if (Directory.Exists(tempExtract)) Directory.Delete(tempExtract, true);
            ZipFile.ExtractToDirectory(zipPath, tempExtract);

            Directory.CreateDirectory(engineDir);
            var dirs = Directory.GetDirectories(tempExtract);
            if (dirs.Length > 0)
            {
                foreach (var file in Directory.GetFiles(dirs[0], "*", SearchOption.AllDirectories))
                {
                    string relativePath = Path.GetRelativePath(dirs[0], file);
                    string destPath = Path.Combine(engineDir, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    File.Move(file, destPath, true);
                }
            }

            try { File.Delete(zipPath); } catch { }
            try { Directory.Delete(tempExtract, true); } catch { }

            try
            {
                Directory.CreateDirectory(EngineCacheDir);
                foreach (var exe in Directory.GetFiles(engineDir, "stockfish*.exe", SearchOption.AllDirectories))
                {
                    string cached = Path.Combine(EngineCacheDir, Path.GetFileName(exe));
                    File.Copy(exe, cached, overwrite: true);
                }
                Log($"Chess engine cached to {EngineCacheDir} for future installs");
            }
            catch (Exception ex)
            {
                Log($"Could not cache engine: {ex.Message}");
            }

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

        private async Task<bool> SaveLicenseAsync(string licenseKey)
        {
            string escaped = licenseKey.Replace("\\", "\\\\").Replace("'", "\\'");
            string script =
                $"import sys; sys.path.insert(0, r'{_installDir}'); " +
                $"import license as L; L.activate('{escaped}')";

            var psi = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = $"-c \"{script.Replace("\"", "\\\"")}\"",
                WorkingDirectory = _installDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
            };
            psi.Environment["PYTHONIOENCODING"] = "utf-8";
            psi.Environment["PYTHONUTF8"] = "1";

            try
            {
                using var p = Process.Start(psi)!;
                string stderr = await p.StandardError.ReadToEndAsync();
                await p.WaitForExitAsync();
                if (p.ExitCode != 0)
                {
                    Log($"[LICENSE] activate failed (exit {p.ExitCode}): {stderr.Trim()}");
                    return false;
                }
                string licPath = Path.Combine(_installDir, "license.dat");
                return File.Exists(licPath);
            }
            catch (Exception ex)
            {
                Log($"[LICENSE] activate error: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> ArePipRequirementsSatisfiedAsync(string installDir)
        {
            string reqPath = Path.Combine(installDir, "requirements.txt");
            if (!File.Exists(reqPath)) return true;

            var tcs = new TaskCompletionSource<bool>();
            Process p = new Process();
            p.StartInfo.FileName = "python";
            p.StartInfo.Arguments = "-m pip install --dry-run --no-deps --quiet -r requirements.txt";
            p.StartInfo.WorkingDirectory = installDir;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.RedirectStandardError = true;
            p.EnableRaisingEvents = true;

            bool wouldInstall = false;
            p.OutputDataReceived += (s, e) => { if (e.Data != null && e.Data.Contains("Would install", StringComparison.OrdinalIgnoreCase)) wouldInstall = true; };
            p.ErrorDataReceived += (s, e) => { };
            p.Exited += (s, e) => tcs.SetResult(p.ExitCode == 0 && !wouldInstall);

            try
            {
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
            }
            catch
            {
                return false;
            }

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(15000));
            if (completed != tcs.Task) { try { p.Kill(true); } catch { } return false; }
            return await tcs.Task;
        }

        private Task InstallPipRequirementsAsync(string installDir)
        {
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();

            string wheelsDir = Path.Combine(installDir, "wheels");
            bool hasWheels = Directory.Exists(wheelsDir) &&
                             Directory.EnumerateFiles(wheelsDir, "*.whl").Any();

            string args;
            if (hasWheels)
            {
                args = $"-m pip install --no-index --find-links \"{wheelsDir}\" " +
                       "--disable-pip-version-check -q -r requirements.txt";
                Log($"Using bundled wheels from {wheelsDir}");
            }
            else
            {
                args = "-m pip install --prefer-binary --disable-pip-version-check " +
                       "-q -r requirements.txt";
            }

            Process p = new Process();
            p.StartInfo.FileName = "python";
            p.StartInfo.Arguments = args;
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
                string guiExe = Path.Combine(_installDir, "LichessBotGUI", "LichessBotGUI.exe");
                string uninstallExe = Path.Combine(_installDir, "LichessBotUninstall.exe");
                string setupExe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                string uninstallTarget = File.Exists(uninstallExe) ? uninstallExe : setupExe;
                string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

                long sizeKB = 0;
                try { sizeKB = new System.IO.DirectoryInfo(_installDir).GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) / 1024; } catch { }

                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\LichessBot");
                key.SetValue("DisplayName", "Lichess Bot");
                key.SetValue("DisplayVersion", version);
                key.SetValue("Publisher", "Toliya-max");
                key.SetValue("InstallLocation", _installDir);
                key.SetValue("DisplayIcon", $"\"{guiExe}\"");
                key.SetValue("UninstallString", $"\"{uninstallTarget}\"");
                key.SetValue("QuietUninstallString", $"\"{uninstallTarget}\" /silent");
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

        private void AutoConfigureEngine()
        {
            string settingsPath = Path.Combine(_installDir, "settings.json");
            if (File.Exists(settingsPath))
            {
                Log("Settings already exist — skipping auto-config.");
                return;
            }

            try
            {
                int cpuCores = Environment.ProcessorCount;
                long totalRamMB = 0;
                try
                {
                    totalRamMB = (long)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024);
                }
                catch
                {
                    totalRamMB = 8192;
                }

                int threads = Math.Max(1, Math.Min(cpuCores - 1, 16));
                int hashMB = totalRamMB switch
                {
                    >= 32768 => 8192,
                    >= 16384 => 4096,
                    >= 8192  => 2048,
                    >= 4096  => 1024,
                    _        => 256
                };
                int moveOverhead = cpuCores >= 8 ? 50 : 100;

                var config = new
                {
                    AutoChallenger = true,
                    Rated = false,
                    AutoResign = true,
                    ResignThreshold = "-5.0",
                    MinRating = "1900",
                    MaxGames = "0",
                    BaseTime = "3",
                    Increment = "0",
                    EnginePath = "Default Stockfish 18",
                    BookPath = "Default gm_openings.bin",
                    UseNNUE = true,
                    SkillLevel = 20.0,
                    MoveSpeed = 1.0,
                    MaxDepth = "0",
                    Ponder = false,
                    Threads = threads,
                    Hash = hashMB,
                    MoveOverhead = moveOverhead.ToString(),
                    VariantIndex = 0,
                    ColorIndex = 0,
                    SendChat = true,
                    Greeting = "glhf!",
                    GGMessage = "gg wp!",
                    AcceptRematch = true
                };

                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(settingsPath, json);

                Log($"[HARDWARE] CPU: {cpuCores} cores, RAM: {totalRamMB} MB");
                Log($"[HARDWARE] Auto-configured: Threads={threads}, Hash={hashMB}MB, Overhead={moveOverhead}ms");
            }
            catch (Exception ex)
            {
                Log($"Auto-config skipped: {ex.Message}");
            }
        }

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

        private async void BtnUninstall_Click(object sender, RoutedEventArgs e)
        {
            string uninstallExe = Path.Combine(_installDir, "LichessBotUninstall.exe");
            if (File.Exists(uninstallExe))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = uninstallExe,
                        UseShellExecute = true,
                    });
                    Application.Current.Shutdown();
                    return;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this,
                        $"Could not start the uninstaller:\n{ex.Message}\n\nFalling back to built-in cleanup.",
                        "Uninstaller", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            var result = MessageBox.Show(this,
                $"This will permanently delete all Lichess Bot files from:\n\n{_installDir}\n\nAnd the Desktop shortcut.\n\nAre you sure?",
                "Confirm Uninstall", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                KillBotProcesses();
                await Task.Delay(400);

                if (Directory.Exists(_installDir))
                {
                    bool ok = await ForceDeleteDirectoryAsync(_installDir);
                    if (!ok)
                        throw new IOException(
                            $"Could not delete {_installDir}. " +
                            "Close all processes in Task Manager or reboot, then retry.");
                }

                string shortcutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Lichess Bot.lnk");
                if (File.Exists(shortcutPath))
                {
                    try { File.Delete(shortcutPath); } catch { }
                }

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
