namespace NetworkMonitor.Core;

/// <summary>
/// Traffic-table keys. Interfaces use their native ids; monitored processes are stored
/// under the same schema with a "P:" prefix so every existing day/hour query works for them.
/// </summary>
public static class TrafficKeys
{
    public const string ProcessPrefix = "P:";

    public static string ForProcess(string name) => ProcessPrefix + name;

    public static bool IsProcess(string key) => key.StartsWith(ProcessPrefix, StringComparison.Ordinal);

    public static string ProcessDisplayName(string key) =>
        IsProcess(key) ? Path.GetFileNameWithoutExtension(key[ProcessPrefix.Length..]) + ".exe" : key;
}
