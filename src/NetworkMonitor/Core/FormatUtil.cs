using System.Diagnostics;

namespace NetworkMonitor.Core;

public static class DateUtil
{
    /// <summary>Local date key formatted as yyyyMMdd (sortable integer).</summary>
    public static int DayKey(DateTime local) => local.Year * 10000 + local.Month * 100 + local.Day;

    public static int DayKeyOf(int day, int offsetDays) => DayKey(FromDayKey(day).AddDays(offsetDays));

    public static DateTime FromDayKey(int key) => new(key / 10000, (key / 100) % 100, key % 100);

    /// <summary>Unix seconds of the start of the local minute containing <paramref name="local"/>.</summary>
    public static long MinuteTimestamp(DateTime local)
    {
        var floored = new DateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0, DateTimeKind.Local);
        return new DateTimeOffset(floored).ToUnixTimeSeconds();
    }
}

public static class FormatUtil
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB", "PB" };

    /// <summary>Formats a byte count, 1000-based (KB = 1000 B).</summary>
    public static string Bytes(long bytes)
    {
        double v = bytes;
        int u = 0;
        while (v >= 1000 && u < Units.Length - 1)
        {
            v /= 1000;
            u++;
        }

        return u == 0
            ? $"{v:0} {Units[u]}"
            : v >= 100 ? $"{v:0} {Units[u]}" : v >= 10 ? $"{v:0.#} {Units[u]}" : $"{v:0.##} {Units[u]}";
    }

    /// <summary>Formats a transfer rate (bytes per second).</summary>
    public static string Rate(long bytesPerSecond) => Bytes(bytesPerSecond) + "/s";

    public static string Rate2(long bytesPerSecond) => $"{Rate(bytesPerSecond)}";
}

public static class AppInfo
{
    public static string ExePath =>
        Environment.ProcessPath
        ?? Process.GetCurrentProcess().MainModule?.FileName
        ?? Application.ExecutablePath;
}
