using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace LichessBotGUI
{
    public partial class ActivationWindow : Window
    {
        public bool IsActivated { get; private set; } = false;

        private readonly string _pythonPath;
        private readonly string _botDirectory;
        private readonly bool _isManageMode;
        private readonly string? _currentKey;
        private readonly string? _currentInfo;

        public ActivationWindow(
            string pythonPath,
            string botDirectory,
            bool isManageMode = false,
            string? currentKey = null,
            string? currentInfo = null,
            string? currentApiToken = null)
        {
            InitializeComponent();
            _pythonPath = pythonPath;
            _botDirectory = botDirectory;
            _isManageMode = isManageMode;
            _currentKey = currentKey;
            _currentInfo = currentInfo;

            if (!string.IsNullOrEmpty(currentApiToken))
                TxtApiToken.Password = currentApiToken;

            if (_isManageMode)
                SetupManageMode();
        }

        private void SetupManageMode()
        {
            if (!string.IsNullOrEmpty(_currentInfo))
                ShowStatus($"Current license: {_currentInfo}", isError: false);

            if (!string.IsNullOrEmpty(_currentKey))
                TxtKey.Text = _currentKey;

            BtnActivate.Content = "Update";
            BtnExit.Content = "Close";
        }

        private async void BtnActivate_Click(object sender, RoutedEventArgs e)
        {
            string apiToken = TxtApiToken.Password.Trim();
            string key = TxtKey.Text.Trim();

            if (string.IsNullOrEmpty(apiToken))
            {
                ShowStatus("Please enter your Lichess API token.", isError: true);
                return;
            }

            if (string.IsNullOrEmpty(key))
            {
                ShowStatus("Please enter a license key.", isError: true);
                return;
            }

            BtnActivate.IsEnabled = false;
            BtnExit.IsEnabled = false;
            ShowStatus("Validating key...", isError: false, isPending: true);

            SaveApiToken(apiToken);
            var result = await Task.Run(() => RunActivation(key));

            BtnActivate.IsEnabled = true;
            BtnExit.IsEnabled = true;

            if (result.Success)
            {
                ShowStatus($"Activated! {result.Info}", isError: false);
                IsActivated = true;
                await Task.Delay(1200);
                Close();
            }
            else
            {
                ShowStatus(result.Error ?? "Activation failed.", isError: true);
            }
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            if (_isManageMode)
                Close();
            else
                Application.Current.Shutdown();
        }

        private void SaveApiToken(string token)
        {
            string envPath = Path.Combine(_botDirectory, ".env");
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

        private record ActivationResult(bool Success, string? Info, string? Error);

        private ActivationResult RunActivation(string key)
        {
            string safeKey = key.Replace("'", "").Replace("\"", "");

            string script =
                $"import sys; sys.path.insert(0, r'{_botDirectory}'); " +
                $"import license as L; " +
                $"info = L.activate('{safeKey}'); " +
                $"print(f\"{{info['type']}} — expires {{info['expiry']}} ({{info['days_left']}} days)\")";

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = _pythonPath,
                    Arguments = $"-c \"{script.Replace("\"", "\\\"")}\"",
                    WorkingDirectory = _botDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                using var proc = Process.Start(psi)!;
                string stdout = proc.StandardOutput.ReadToEnd().Trim();
                string stderr = proc.StandardError.ReadToEnd().Trim();
                proc.WaitForExit();

                if (proc.ExitCode == 0 && !string.IsNullOrEmpty(stdout))
                    return new ActivationResult(true, stdout, null);

                string err = stderr;
                if (err.Contains("LicenseError:"))
                    err = err.Substring(err.LastIndexOf("LicenseError:") + "LicenseError:".Length).Trim();
                else if (string.IsNullOrEmpty(err))
                    err = "Unknown error — check that Python dependencies are installed.";

                return new ActivationResult(false, null, err);
            }
            catch (Exception ex)
            {
                return new ActivationResult(false, null, ex.Message);
            }
        }

        private void ShowStatus(string message, bool isError, bool isPending = false)
        {
            Dispatcher.Invoke(() =>
            {
                TxtStatus.Text = message;
                if (isError)
                {
                    TxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xe8, 0x50, 0x40));
                    StatusBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xb8, 0x38, 0x28));
                }
                else if (isPending)
                {
                    TxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xd4, 0x98, 0x5a));
                    StatusBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xd4, 0x98, 0x5a));
                }
                else
                {
                    TxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x6a, 0x9b, 0x2c));
                    StatusBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x6a, 0x9b, 0x2c));
                }
            });
        }
    }
}
