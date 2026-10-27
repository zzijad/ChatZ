using System.Threading;
using System.Windows;

namespace ChatZ;

public partial class App : Application
{
    private const string MutexName = "ChatZ_SingleInstance";
    private const string ShowEventName = "ChatZ_ShowWindow";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, MutexName, out bool isFirst);
        if (!isFirst)
        {
            // Već radi — samo kažemo postojećem prozoru da se pokaže.
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
            Shutdown();
            return;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

        base.OnStartup(e);

        bool startInTray = e.Args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
        var window = new MainWindow();
        MainWindow = window;
        if (!startInTray) window.Show();
        else window.InitializeHidden();

        var listener = new Thread(() =>
        {
            while (_showEvent.WaitOne())
                Dispatcher.Invoke(window.ShowFromTray);
        }) { IsBackground = true };
        listener.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showEvent?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
