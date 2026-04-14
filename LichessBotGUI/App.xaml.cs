using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace LichessBotGUI;

public partial class App : Application
{
    private DispatcherTimer? _debugTimer;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        KillIfDebugged();
        _debugTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(20)
        };
        _debugTimer.Tick += (_, _) => KillIfDebugged();
        _debugTimer.Start();
    }

    private static void KillIfDebugged()
    {
#if !DEBUG
        if (Debugger.IsAttached)
            Environment.Exit(0);
#endif
    }
}
