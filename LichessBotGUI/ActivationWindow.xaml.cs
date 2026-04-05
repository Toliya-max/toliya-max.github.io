using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace LichessBotGUI
{
    public partial class ActivationWindow : Window
    {
        // Set to true when activation succeeds — caller checks this.
        public bool IsActivated { get; private set; } = false;

        // Path to the Python interpreter (venv or system).
        private readonly string _pythonPath;
        // Working directory of the bot (where license.py lives).
        private readonly string _botDirectory;

        public ActivationWindow(string pythonPath, string botDirectory)
        {
            InitializeComponent();
            _pythonPath = pythonPath;
            _botDirectory = botDirectory;
        }

        private async void BtnActivate_Click(object sender, RoutedEventArgs e)
        {
            string key = TxtKey.Text.Trim();
            if (string.IsNullOrEmpty(key))
            {
                ShowStatus("Please enter a license key.", isError: true);
                return;
            }

            BtnActivate.IsEnabled = false;
            BtnExit.IsEnabled = false;
            ShowStatus("Validating key...", isError: false, isPending: true);

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
            Application.Current.Shutdown();
        }

        // ─── run python -c "import license; ..." ─────────────────────────────

        private record ActivationResult(bool Success, string? Info, string? Error);

        private ActivationResult RunActivation(string key)
        {
            // Escape single quotes in key just in case
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

                // Parse LicenseError message from stderr
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

        // ─── UI helpers ───────────────────────────────────────────────────────

        private void ShowStatus(string message, bool isError, bool isPending = false)
        {
            Dispatcher.Invoke(() =>
            {
                TxtStatus.Text = message;
                if (isError)
                {
                    TxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xf8, 0x51, 0x49));
                    StatusBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xc9, 0x37, 0x2c));
                }
                else if (isPending)
                {
                    TxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xe3, 0x9a, 0x00));
                    StatusBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xe3, 0x9a, 0x00));
                }
                else
                {
                    TxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x3f, 0xb9, 0x50));
                    StatusBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3f, 0xb9, 0x50));
                }
            });
        }
    }
}
