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

        // "--takeover [pid]": an elevated instance replacing a non-elevated one. Waiting for
        // the old process to actually exit makes the mutex hand-over deterministic; the
        // previous WaitOne-only approach could lose the race, wake the old instance and
        // exit, which looked like "the restart as administrator did nothing".
        bool takeover = args.Any(a => a.Equals("--takeover", StringComparison.OrdinalIgnoreCase));
        int takeoverPid = 0;
        if (takeover)
        {
            int idx = Array.FindIndex(args, a => a.Equals("--takeover", StringComparison.OrdinalIgnoreCase));
            if (idx >= 0 && idx + 1 < args.Length) int.TryParse(args[idx + 1], out takeoverPid);
        }

        Diag.Log($"start takeover={takeover} oldPid={takeoverPid} elevated={ProcessTrafficMonitor.IsElevated}");

        if (takeover && takeoverPid > 0) EnsureOldInstanceGone(takeoverPid, TimeSpan.FromSeconds(10));

        var mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew && takeover)
        {
            // the old process may still be releasing its handle: retry briefly
            for (int i = 0; i < 12 && !createdNew; i++)
            {
                Thread.Sleep(500);
                mutex.Dispose();
                mutex = new Mutex(true, MutexName, out createdNew);
            }

            Diag.Log($"takeover mutex acquired={createdNew}");
        }

        if (!createdNew)
        {
            // Another instance is already running: ask it to show its window.
            Diag.Log("second instance -> waking the existing one");
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
    /// Waits for the previous instance to exit; if it is still alive after the timeout it is
    /// terminated (same application, explicitly handing over) so the elevation always wins.
    /// </summary>
    private static void EnsureOldInstanceGone(int pid, TimeSpan timeout)
    {
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(pid);
            if (p.WaitForExit((int)timeout.TotalMilliseconds))
            {
                Diag.Log($"old instance {pid} exited");
                return;
            }

            Diag.Log($"old instance {pid} still alive after {timeout.TotalSeconds:0}s - terminating it");
            try { p.Kill(); } catch { /* ignore */ }
            p.WaitForExit(3000);
        }
        catch
        {
            // already gone
        }
    }

    /// <summary>Waits for another process to exit (used by the elevation hand-over).</summary>
    private static void WaitForExit(int pid, TimeSpan timeout)
    {
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(pid);
            if (!p.WaitForExit((int)timeout.TotalMilliseconds))
                Diag.Log($"wait for pid {pid} timed out");
        }
        catch
        {
            // already gone
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
