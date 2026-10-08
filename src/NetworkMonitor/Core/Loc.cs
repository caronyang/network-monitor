namespace NetworkMonitor.Core;

/// <summary>
/// Tiny built-in localization table (English / Traditional Chinese).
/// All UI strings are resolved at paint/apply time so a language switch takes effect immediately.
/// </summary>
public static class Loc
{
    public const string English = "en";
    public const string Chinese = "zh";

    public static string Language { get; private set; } = English;

    public static bool IsZh => Language == Chinese;

    public static string NativeName => IsZh ? "繁體中文" : "English";

    /// <summary>Raised after the language changed; listeners re-apply their texts.</summary>
    public static event Action? LanguageChanged;

    private static readonly Dictionary<string, (string En, string Zh)> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        // ---- common ----
        ["app.name"] = ("Network Monitor", "網路監控程式"),
        ["common.save"] = ("Save", "儲存"),
        ["common.cancel"] = ("Cancel", "取消"),
        ["common.close"] = ("Close", "關閉"),
        ["common.ok"] = ("OK", "確定"),
        ["common.language"] = ("Language", "語言"),
        ["common.apply"] = ("Apply", "套用"),
        ["common.error"] = ("Error", "錯誤"),

        // ---- main window ----
        ["main.title"] = ("Network Monitor", "網路監控程式"),
        ["main.down"] = ("Download", "下載"),
        ["main.up"] = ("Upload", "上傳"),
        ["main.total"] = ("Total", "總計"),
        ["main.today"] = ("Today", "今日"),
        ["main.session"] = ("Session", "本次行程"),
        ["main.interfaces"] = ("Interfaces", "網路介面"),
        ["main.interface"] = ("Interface", "介面"),
        ["main.status"] = ("Status", "狀態"),
        ["main.link"] = ("Link", "連線速率"),
        ["main.connected"] = ("Connected", "已連線"),
        ["main.disconnected"] = ("Disconnected", "未連線"),
        ["main.collecting"] = ("Collecting data...", "資料收集中..."),
        ["main.none"] = ("No network interface matches the current filter.", "目前篩選條件下沒有符合的網路介面。"),
        ["main.stats"] = ("Statistics", "統計圖表"),
        ["main.settings"] = ("Settings", "設定"),
        ["main.updated"] = ("Updated {0}", "更新於 {0}"),

        // ---- tray ----
        ["tray.show"] = ("Show main window", "顯示主視窗"),
        ["tray.stats"] = ("Statistics", "統計圖表"),
        ["tray.panel"] = ("Floating panel", "即時浮動面板"),
        ["tray.autoStart"] = ("Start with Windows", "開機自動啟動"),
        ["tray.settings"] = ("Settings", "設定"),
        ["tray.language"] = ("Language", "介面語言"),
        ["tray.exit"] = ("Exit", "結束程式"),
        ["tray.tip"] = ("Network Monitor", "網路監控程式"),

        // ---- floating panel ----
        ["panel.title"] = ("Real-time", "即時監控"),
        ["panel.tip"] = ("Double click: open window / Drag: move / Right click: menu",
                         "雙擊：開啟視窗 / 拖曳：移動 / 右鍵：選單"),

        // ---- statistics ----
        ["stats.title"] = ("Statistics", "統計圖表"),
        ["stats.chart"] = ("Chart", "圖表"),
        ["stats.line"] = ("Line", "折線圖"),
        ["stats.bar"] = ("Bar", "直條圖"),
        ["stats.period"] = ("Period", "期間"),
        ["stats.day"] = ("Day", "日"),
        ["stats.week"] = ("Week", "週"),
        ["stats.month"] = ("Month", "月"),
        ["stats.iface"] = ("Interface", "介面"),
        ["stats.all"] = ("All interfaces", "全部介面"),
        ["stats.prev"] = ("Previous", "上一期"),
        ["stats.next"] = ("Next", "下一期"),
        ["stats.refresh"] = ("Refresh", "重新整理"),
        ["stats.export"] = ("Export CSV", "匯出 CSV"),
        ["stats.loading"] = ("Loading...", "載入中..."),
        ["stats.noData"] = ("No data in this range.", "此區間沒有資料。"),
        ["stats.rx"] = ("Download", "下載"),
        ["stats.tx"] = ("Upload", "上傳"),
        ["stats.totalRx"] = ("Download total", "下載總計"),
        ["stats.totalTx"] = ("Upload total", "上傳總計"),
        ["stats.range"] = ("Range", "區間"),
        ["stats.axis"] = ("Traffic", "傳輸量"),
        ["stats.saved"] = ("Exported to {0}", "已匯出至 {0}"),
        ["stats.dayHint"] = ("Hourly", "分時"),
        ["stats.weekHint"] = ("Daily", "分日"),
        ["stats.monthHint"] = ("Daily", "分日"),

        // ---- settings ----
        ["settings.title"] = ("Settings", "設定"),
        ["settings.general"] = ("General", "一般"),
        ["settings.language"] = ("Interface language", "介面語言"),
        ["settings.autoStart"] = ("Start automatically after Windows login", "Windows 開機後自動執行"),
        ["settings.panel"] = ("Show floating real-time panel", "顯示即時浮動面板"),
        ["settings.startMin"] = ("Start minimized to tray", "啟動時最小化至系統匣"),
        ["settings.minTray"] = ("Close button minimizes to tray", "按關閉鈕時最小化至系統匣"),
        ["settings.monitor"] = ("Monitoring", "監控設定"),
        ["settings.interval"] = ("Sampling interval (ms)", "取樣間隔 (毫秒)"),
        ["settings.retention"] = ("Keep raw data (days)", "原始資料保留天數"),
        ["settings.loopback"] = ("Include loopback interface (127.0.0.1)", "包含回送介面 (127.0.0.1)"),
        ["settings.down"] = ("Include disconnected interfaces", "包含未連線的介面"),
        ["settings.filter"] = ("Include packet-filter adapters (WFP/Npcap/QoS, duplicates parent traffic)",
                               "包含封包篩選介面（WFP/Npcap/QoS，會與實體介面重複計算）"),
        ["settings.data"] = ("Data", "資料"),
        ["settings.openFolder"] = ("Open data folder", "開啟資料資料夾"),
        ["settings.clear"] = ("Delete all records", "刪除所有紀錄"),
        ["settings.clearAsk"] = ("Delete ALL recorded traffic data? This cannot be undone.",
                                 "確定刪除所有已記錄的傳輸量資料？此動作無法復原。"),
        ["settings.dbInfo"] = ("Database: {0}  ·  {1} records", "資料庫：{0}  ·  {1} 筆紀錄"),
        ["settings.resolution"] = ("Recording resolution: 1 minute per interface.",
                                   "紀錄解析度：每個介面每分鐘一筆。"),
        ["settings.intervalRange"] = ("200 - 60000 ms", "200 - 60000 毫秒"),
        ["settings.retentionRange"] = ("1 - 3650 days", "1 - 3650 天"),
    };

    public static void Init(string language) => Language = language == Chinese ? Chinese : English;

    public static void SetLanguage(string language)
    {
        var next = language == Chinese ? Chinese : English;
        if (next == Language) return;
        Language = next;
        LanguageChanged?.Invoke();
    }

    public static void Toggle() => SetLanguage(IsZh ? English : Chinese);

    /// <summary>Looks up a key and formats it with the supplied arguments.</summary>
    public static string T(string key, params object[] args)
    {
        string text = Resolve(key);
        if (args.Length == 0) return text;
        try
        {
            return string.Format(text, args);
        }
        catch (FormatException)
        {
            return text;
        }
    }

    private static string Resolve(string key)
    {
        if (Table.TryGetValue(key, out var v)) return IsZh ? v.Zh : v.En;
        return key;
    }

    /// <summary>Re-applies localized texts on every open form.</summary>
    public static void RefreshOpenForms()
    {
        Form[] forms;
        try
        {
            forms = Application.OpenForms.Cast<Form>().ToArray();
        }
        catch
        {
            return;
        }

        foreach (var f in forms)
        {
            if (f.IsDisposed) continue;
            if (f is ILocalizable loc)
            {
                try { loc.ApplyLocalization(); }
                catch { /* a disposing form must not break the switch */ }
            }
        }
    }
}

public interface ILocalizable
{
    void ApplyLocalization();
}
