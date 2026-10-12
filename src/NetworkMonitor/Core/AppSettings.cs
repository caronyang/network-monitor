using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetworkMonitor.Core;

/// <summary>Application settings persisted as JSON in %LOCALAPPDATA%\NetworkMonitor\settings.json.</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    [JsonPropertyName("language")]
    public string Language { get; set; } = "en"; // "en" | "zh"

    [JsonPropertyName("autoStart")]
    public bool AutoStart { get; set; }

    [JsonPropertyName("floatingPanel")]
    public bool FloatingPanel { get; set; }

    [JsonPropertyName("startMinimized")]
    public bool StartMinimized { get; set; }

    [JsonPropertyName("minimizeToTray")]
    public bool MinimizeToTray { get; set; } = true;

    [JsonPropertyName("sampleIntervalMs")]
    public int SampleIntervalMs { get; set; } = 1000;

    [JsonPropertyName("retentionDays")]
    public int RetentionDays { get; set; } = 180;

    [JsonPropertyName("includeLoopback")]
    public bool IncludeLoopback { get; set; }

    [JsonPropertyName("includeDisconnected")]
    public bool IncludeDisconnected { get; set; }

    [JsonPropertyName("showFilterAdapters")]
    public bool ShowFilterAdapters { get; set; }

    [JsonPropertyName("processMonitors")]
    public List<string> ProcessMonitors { get; set; } = new();

    [JsonPropertyName("panelX")]
    public int? PanelX { get; set; }

    [JsonPropertyName("panelY")]
    public int? PanelY { get; set; }

    [JsonPropertyName("mainWidth")]
    public int? MainWidth { get; set; }

    [JsonPropertyName("mainHeight")]
    public int? MainHeight { get; set; }

    public static string DataFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetworkMonitor");

    public static string SettingsFile => Path.Combine(DataFolder, "settings.json");

    public static string DatabaseFile => Path.Combine(DataFolder, "networkmonitor.db");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                var s = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts);
                if (s is not null) return s.Normalize();
            }
        }
        catch
        {
            // fall through to defaults - never fail to start because of a bad settings file
        }

        return new AppSettings().Normalize();
    }

    public AppSettings Normalize()
    {
        Language = Language is "zh" or "en" ? Language : "en";
        SampleIntervalMs = Math.Clamp(SampleIntervalMs, 200, 60_000);
        RetentionDays = Math.Clamp(RetentionDays, 1, 3650);

        // process monitor list: canonical lowercase base names, deduped, hard cap of 5
        ProcessMonitors ??= new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var clean = new List<string>(Math.Min(5, ProcessMonitors.Count + 1));
        foreach (var raw in ProcessMonitors)
        {
            var name = Path.GetFileNameWithoutExtension((raw ?? "").Trim()).ToLowerInvariant();
            if (name.Length == 0 || !seen.Add(name)) continue;
            clean.Add(name);
            if (clean.Count >= MaxProcessMonitors) break;
        }
        ProcessMonitors = clean;
        return this;
    }

    public const int MaxProcessMonitors = 5;

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataFolder);
            var tmp = SettingsFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOpts));
            File.Copy(tmp, SettingsFile, true);
            File.Delete(tmp);
        }
        catch
        {
            // ignore persistence errors (file locked / disk full)
        }
    }
}
