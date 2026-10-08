namespace NetworkMonitor.Core;

/// <summary>Appends unhandled exceptions to a log file instead of blocking the UI with dialogs.</summary>
public static class CrashLog
{
    private static readonly object Gate = new();

    public static string File => Path.Combine(AppSettings.DataFolder, "crash.log");

    public static void Write(Exception? ex)
    {
        if (ex is null) return;
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppSettings.DataFolder);
                System.IO.File.AppendAllText(File,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex.GetType().Name}: {ex.Message}\r\n{ex.StackTrace}\r\n\r\n");
            }
        }
        catch
        {
            // logging must never throw
        }
    }
}
