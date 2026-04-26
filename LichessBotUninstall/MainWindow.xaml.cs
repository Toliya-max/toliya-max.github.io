using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LichessBotUninstall
{
    public partial class MainWindow : Window
    {
        private readonly string _installDir;
        private readonly string _secretsCacheDir;
        private readonly string _engineCacheDir;
        private readonly string _shortcutPath;
        private const string UninstallRegPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\LichessBot";
        private bool _silent;

        private static readonly string[] BotProcessNames =
        {
            "LichessBotGUI",
            "pythonw", "python", "py",
            "stockfish", "stockfish-windows-x86-64-avx2", "stockfish_18",
            "cli", "bot",
        };

        public MainWindow()
        {
            InitializeComponent();

            _installDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LichessBot");

            _secretsCacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LichessBot-cache", "secrets");

            _engineCacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LichessBot-cache", "stockfish");

            _shortcutPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "Lichess Bot.lnk");

            try
            {
                string? v = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3);
                if (!string.IsNullOrEmpty(v))
                {
                    TitleVersion.Text = $"Uninstall Lichess Bot v{v}";
                    this.Title = $"Uninstall Lichess Bot v{v}";
                }
            }
            catch { }

            TxtPathLine.Text = $"• Install folder: {_installDir}";

            string[] args = Environment.GetCommandLineArgs();
            _silent = args.Skip(1).Any(a =>
                a.Equals("/silent", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("/quiet", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--silent", StringComparison.OrdinalIgnoreCase));

            if (!Directory.Exists(_installDir))
            {
                BtnUninstall.IsEnabled = false;
                BtnUninstall.Content = "Nothing to remove";
                TxtPathLine.Text = $"• Install folder: {_installDir}  (not found)";
            }

            if (_silent)
            {
                Loaded += async (_, _) => await RunUninstallAsync(removeCache: true, autoClose: true);
            }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) =>
            Application.Current.Shutdown();

        private void BtnCancel_Click(object sender, RoutedEventArgs e) =>
            Application.Current.Shutdown();

        private void BtnFinish_Click(object sender, RoutedEventArgs e) =>
            Application.Current.Shutdown();

        private async void BtnUninstall_Click(object sender, RoutedEventArgs e)
        {
            await RunUninstallAsync(ChkRemoveCache.IsChecked == true, autoClose: false);
        }

        private async void BtnRetry_Click(object sender, RoutedEventArgs e)
        {
            await RunUninstallAsync(ChkRemoveCache.IsChecked == true, autoClose: false);
        }

        private async Task RunUninstallAsync(bool removeCache, bool autoClose)
        {
            ShowPage("progress");
            BtnCancel.Visibility = Visibility.Collapsed;
            BtnUninstall.Visibility = Visibility.Collapsed;
            BtnRetry.Visibility = Visibility.Collapsed;
            TxtLog.Text = "";
            SetProgress(0);

            try
            {
                Log("Stopping running Lichess Bot processes...");
                KillBotProcesses();
                await Task.Delay(400);
                SetProgress(15);

                if (Directory.Exists(_installDir))
                {
                    Log($"Removing install folder: {_installDir}");
                    bool ok = await ForceDeleteDirectoryAsync(_installDir);
                    if (!ok)
                        throw new IOException(
                            $"Could not delete {_installDir}. Close all Lichess Bot processes in Task Manager and try again.");
                    Log("Install folder removed.");
                }
                else
                {
                    Log("Install folder not present, skipping.");
                }
                SetProgress(55);

                if (File.Exists(_shortcutPath))
                {
                    try
                    {
                        File.Delete(_shortcutPath);
                        Log("Desktop shortcut removed.");
                    }
                    catch (Exception ex)
                    {
                        Log($"Could not delete desktop shortcut: {ex.Message}");
                    }
                }
                SetProgress(70);

                try
                {
                    Microsoft.Win32.Registry.CurrentUser.DeleteSubKey(UninstallRegPath, throwOnMissingSubKey: false);
                    Log("Removed Apps & Features registration.");
                }
                catch (Exception ex)
                {
                    Log($"Could not remove registry entry: {ex.Message}");
                }
                SetProgress(85);

                if (removeCache)
                {
                    foreach (var dir in new[] { _secretsCacheDir, _engineCacheDir })
                    {
                        if (Directory.Exists(dir))
                        {
                            bool ok = await ForceDeleteDirectoryAsync(dir);
                            Log(ok
                                ? $"Removed cache: {dir}"
                                : $"Warning: could not remove cache: {dir}");
                        }
                    }
                    string cacheRoot = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "LichessBot-cache");
                    try
                    {
                        if (Directory.Exists(cacheRoot) && !Directory.EnumerateFileSystemEntries(cacheRoot).Any())
                            Directory.Delete(cacheRoot);
                    }
                    catch { }
                }
                else
                {
                    Log("License and API token preserved in cache for future install.");
                }
                SetProgress(100);

                Log("Uninstall complete.");
                TxtCompleteSub.Text = removeCache
                    ? "All program files, cached credentials and registry entries were removed."
                    : "Program removed. Your license and API token are kept in cache so a future install restores them.";
                ShowPage("complete");
                BtnFinish.Visibility = Visibility.Visible;

                if (autoClose)
                {
                    await Task.Delay(1200);
                    Application.Current.Shutdown();
                }
            }
            catch (Exception ex)
            {
                Log($"ERROR: {ex.Message}");
                TxtErrorDetail.Text = ex.Message;
                ShowPage("error");
                BtnRetry.Visibility = Visibility.Visible;
                BtnCancel.Visibility = Visibility.Visible;
            }
        }

        private void ShowPage(string page)
        {
            Dispatcher.Invoke(() =>
            {
                PageConfirm.Visibility = page == "confirm" ? Visibility.Visible : Visibility.Collapsed;
                PageProgress.Visibility = page == "progress" ? Visibility.Visible : Visibility.Collapsed;
                PageComplete.Visibility = page == "complete" ? Visibility.Visible : Visibility.Collapsed;
                PageError.Visibility = page == "error" ? Visibility.Visible : Visibility.Collapsed;
            });
        }

        private void Log(string message)
        {
            Dispatcher.Invoke(() =>
            {
                TxtLog.Text += $"[{DateTime.Now:HH:mm:ss}] {message}\n";
                LogScroll.ScrollToEnd();
            });
        }

        private void SetProgress(double percent)
        {
            Dispatcher.Invoke(() =>
            {
                double maxWidth = PageProgress.ActualWidth > 60 ? PageProgress.ActualWidth - 60 : 540;
                ProgressFill.Width = maxWidth * (percent / 100.0);
            });
        }

        private bool ProcessHoldsPath(Process p, string root)
        {
            try
            {
                string? file = p.MainModule?.FileName;
                if (string.IsNullOrEmpty(file)) return false;
                return file.StartsWith(root, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private void KillBotProcesses()
        {
            int killed = 0;
            foreach (string name in BotProcessNames)
            {
                try
                {
                    foreach (Process proc in Process.GetProcessesByName(name))
                    {
                        try
                        {
                            bool isPython = name == "python" || name == "pythonw" || name == "py" ||
                                            name == "cli" || name == "bot";
                            if (isPython && !ProcessHoldsPath(proc, _installDir))
                                continue;

                            if (proc.Id == Environment.ProcessId) continue;

                            proc.Kill(entireProcessTree: true);
                            proc.WaitForExit(3000);
                            killed++;
                            Log($"Terminated: {name}.exe (pid {proc.Id})");
                        }
                        catch { }
                    }
                }
                catch { }
            }
            if (killed == 0) Log("No active Lichess Bot processes.");
        }

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

                    KillBotProcesses();
                    await Task.Delay(400 * attempt);
                }
            }

            try
            {
                string bak = path + "_old_" + DateTime.Now.Ticks;
                Directory.Move(path, bak);
                Log($"Could not delete in place, moved aside: {bak}");
                try { Directory.Delete(bak, recursive: true); } catch { }
                return !Directory.Exists(path);
            }
            catch (Exception ex)
            {
                Log($"ForceDelete failed completely: {ex.Message}");
                return false;
            }
        }
    }
}
