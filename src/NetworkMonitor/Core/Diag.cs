namespace NetworkMonitor.Core;

/// <summary>
/// Minimal append-only diagnostics log (%LOCALAPPDATA%\NetworkMonitor\diagnostics.log).
/// Records elevation hand-overs and ETW start results so a failing
/// "restart as administrator" can be diagnosed after the fact.
/// </summary>
public static class Diag
{
    public static string LogFile => Path.Combine(AppSettings.DataFolder, "diagnostics.log");

    public static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.DataFolder);
            File.AppendAllText(LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch
        {
            // diagnostics must never break the app
        }
    }
}
