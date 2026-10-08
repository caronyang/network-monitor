using NetworkMonitor.Core;
using NetworkMonitor.UI;

namespace NetworkMonitor;

internal static class Program
{
    private const string MutexName = @"Local\NetworkMonitor_SingleInstance";
    private const string WakeName = "NetworkMonitor_WakeEvent";

    [STAThread]
    private static void Main(string[] args)
    {
        var mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            // Another instance is already running: ask it to show its window.
            try
            {
                using var wake = EventWaitHandle.OpenExisting(WakeName);
                wake.Set();
            }
            catch
            {
                // the other instance may be shutting down
            }

            mutex.Dispose();
            return;
        }

        EventWaitHandle? wakeEvent = null;
        try
        {
            wakeEvent = new EventWaitHandle(false, EventResetMode.AutoReset, WakeName);

            ApplicationConfiguration.Initialize();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => CrashLog.Write(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => CrashLog.Write(e.ExceptionObject as Exception);

            var settings = AppSettings.Load();
            Loc.Init(settings.Language);
            settings.AutoStart = Autostart.IsEnabled();

            Directory.CreateDirectory(AppSettings.DataFolder);
            var db = new TrafficDatabase(AppSettings.DatabaseFile);
            db.EnsureSchema();

            var sampler = new NetworkSampler(settings, db);
            sampler.Start();

            bool startHidden = settings.StartMinimized ||
                                args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));

            var context = new TrayApplicationContext(settings, db, sampler, wakeEvent, !startHidden);
            Application.Run(context);

            sampler.Dispose();
            db.Dispose();
        }
        finally
        {
            wakeEvent?.Dispose();
            if (createdNew)
            {
                try { mutex.ReleaseMutex(); }
                catch { /* already released */ }
            }

            mutex.Dispose();
        }
    }
}
