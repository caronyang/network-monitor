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
        if (args.Any(a => a.Equals("--etw-selftest", StringComparison.OrdinalIgnoreCase)))
        {
            EtwSelfTest();
            return;
        }

        bool takeover = args.Any(a => a.Equals("--takeover", StringComparison.OrdinalIgnoreCase));

        var mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            // --takeover: an elevated instance is replacing a non-elevated one. The old
            // process exits as soon as it has launched us, so wait for it to let go.
            if (takeover)
            {
                try
                {
                    if (mutex.WaitOne(TimeSpan.FromSeconds(8))) goto Acquired;
                }
                catch (AbandonedMutexException)
                {
                    goto Acquired;
                }
            }

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

    Acquired:
        EventWaitHandle? wakeEvent = null;
        ProcessTrafficMonitor? processes = null;
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

            processes = new ProcessTrafficMonitor();
            var sampler = new NetworkSampler(settings, db) { Processes = processes };
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
            try { processes?.Dispose(); } catch { /* ignore */ }
            wakeEvent?.Dispose();
            try { mutex.ReleaseMutex(); }
            catch { /* already released */ }
            mutex.Dispose();
        }
    }

    /// <summary>
    /// Headless ETW probe: starts the kernel-network session, samples a few seconds and
    /// writes %LOCALAPPDATA%\NetworkMonitor\etw_selftest.txt. Run elevated to verify the
    /// per-process source works without launching the UI:
    ///   NetworkMonitor.exe --etw-selftest
    /// </summary>
    private static void EtwSelfTest()
    {
        var m = new ProcessTrafficMonitor();
        m.EnsureRunning();

        // generate a little traffic while the session runs
        using (var wc = new System.Net.Http.HttpClient())
        {
            try { wc.GetStringAsync("https://www.google.com").GetAwaiter().GetResult(); }
            catch { /* offline is fine too */ }
        }

        Thread.Sleep(4000);

        var lines = new List<string>
        {
            $"time={DateTime.Now:O}",
            $"elevated={ProcessTrafficMonitor.IsElevated}",
            $"state={m.State}",
            $"error={m.LastError ?? "-"}",
        };
        foreach (var (name, rx, tx) in m.TopProcesses(20))
            lines.Add($"  {name}: rx={rx:N0} tx={tx:N0}");

        string file = Path.Combine(AppSettings.DataFolder, "etw_selftest.txt");
        Directory.CreateDirectory(AppSettings.DataFolder);
        File.WriteAllLines(file, lines);
        m.Dispose();
    }
}
