using NetworkMonitor.Core;

namespace NetworkMonitor.UI;

/// <summary>
/// Application context: owns the tray icon, the dashboard, the floating panel and the
/// statistics window, and drives all of them from a single 1 Hz UI timer.
/// </summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly AppSettings _settings;
    private readonly TrafficDatabase _db;
    private readonly NetworkSampler _sampler;
    private readonly EventWaitHandle _wakeEvent;
    private readonly Icon _icon;

    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly ToolStripMenuItem _miShow = new();
    private readonly ToolStripMenuItem _miStats = new();
    private readonly ToolStripMenuItem _miPanel = new();
    private readonly ToolStripMenuItem _miAuto = new();
    private readonly ToolStripMenuItem _miSettings = new();
    private readonly ToolStripMenuItem _miExit = new();
    private readonly ToolStripMenuItem _miLang = new();
    private readonly ToolStripMenuItem _miLangEn = new();
    private readonly ToolStripMenuItem _miLangZh = new();

    private readonly MainForm _main;
    private FloatingPanel? _panel;
    private StatsForm? _stats;
    private SettingsForm? _settingsForm;
    private Thread? _wakeThread;
    private volatile bool _stopping;

    public TrayApplicationContext(AppSettings settings, TrafficDatabase db, NetworkSampler sampler,
        EventWaitHandle wakeEvent, bool showMain)
    {
        _settings = settings;
        _db = db;
        _sampler = sampler;
        _wakeEvent = wakeEvent;

        try
        {
            _icon = Icon.ExtractAssociatedIcon(AppInfo.ExePath) ?? SystemIcons.Application;
        }
        catch
        {
            _icon = SystemIcons.Application;
        }

        // ---- dashboard ----
        _main = new MainForm(settings, db);
        _main.RequestStats += ShowStats;
        _main.RequestSettings += ShowSettings;
        _main.FormClosed += (_, _) => Exit();

        // ---- tray ----
        _menu = new ContextMenuStrip { ShowImageMargin = false, Font = new Font("Segoe UI", 9f) };
        _miShow.Click += (_, _) => ShowMain();
        _miStats.Click += (_, _) => ShowStats();
        _miPanel.Click += (_, _) => TogglePanel();
        _miAuto.Click += (_, _) => ToggleAutostart();
        _miSettings.Click += (_, _) => ShowSettings();
        _miExit.Click += (_, _) => Exit();
        _miLangEn.Click += (_, _) => SetLanguage(Loc.English);
        _miLangZh.Click += (_, _) => SetLanguage(Loc.Chinese);

        var langMenu = new ToolStripMenuItem();
        langMenu.DropDownItems.Add(_miLangEn);
        langMenu.DropDownItems.Add(_miLangZh);
        _miLang = langMenu;

        _menu.Items.Add(_miShow);
        _menu.Items.Add(_miStats);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_miPanel);
        _menu.Items.Add(_miAuto);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_miLang);
        _menu.Items.Add(_miSettings);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_miExit);
        _menu.Opening += (_, _) => SyncMenu();

        _tray = new NotifyIcon
        {
            Icon = _icon,
            Text = Loc.T("tray.tip"),
            Visible = true,
            ContextMenuStrip = _menu,
        };
        _tray.DoubleClick += (_, _) => ShowMain();

        Loc.LanguageChanged += OnLanguageChanged;

        // ---- floating panel ----
        if (_settings.FloatingPanel) ShowPanel();

        // ---- 1 Hz UI pump ----
        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) => OnTick();
        _timer.Start();

        // ---- second instance wake-up ----
        _wakeThread = new Thread(WaitForWake) { IsBackground = true, Name = "SingleInstanceWake" };
        _wakeThread.Start();

        if (showMain) ShowMain();
        OnTick();
    }

    // ------------------------------------------------------------------ events

    private void WaitForWake()
    {
        while (!_stopping)
        {
            try
            {
                if (_wakeEvent.WaitOne(500)) BeginInvoke(ShowMain);
            }
            catch
            {
                return;
            }
        }
    }

    private void BeginInvoke(Action action)
    {
        try
        {
            if (_main.IsDisposed) return;
            _main.BeginInvoke(action);
        }
        catch
        {
            // the window handle may be gone during shutdown
        }
    }

    private void OnLanguageChanged()
    {
        Loc.RefreshOpenForms();
        SyncMenu();
        RebuildMenuTexts();
        _tray.Text = TrayText(_sampler.Latest);
    }

    private DateTime _lastTrayTextUtc = DateTime.MinValue;

    private void OnTick()
    {
        if (_stopping) return;
        var snapshot = _sampler.Latest;

        // The tooltip is only visible on hover, so refreshing it every few seconds is
        // imperceptible - and Shell_NotifyIcon round-trips are surprisingly costly.
        if (!DebugFlags.NoTrayText && (DateTime.UtcNow - _lastTrayTextUtc).TotalSeconds >= 5)
        {
            _lastTrayTextUtc = DateTime.UtcNow;
            _tray.Text = TrayText(snapshot);
        }

        _main.PushSnapshot(snapshot);
        _panel?.PushSnapshot(snapshot);
        _stats?.AutoRefresh();
    }

    private static string TrayText(Snapshot snapshot)
    {
        string tip = snapshot.Valid
            ? $"{Loc.T("tray.tip")}\n↓ {FormatUtil.Rate(snapshot.DownRate)}  ↑ {FormatUtil.Rate(snapshot.UpRate)}"
            : Loc.T("tray.tip");

        // NotifyIcon.Text is limited by the shell; keep well below the limit.
        return tip.Length <= 120 ? tip : tip[..120];
    }

    // ------------------------------------------------------------------ menu

    private void RebuildMenuTexts()
    {
        _miShow.Text = Loc.T("tray.show");
        _miStats.Text = Loc.T("tray.stats");
        _miPanel.Text = Loc.T("tray.panel");
        _miAuto.Text = Loc.T("tray.autoStart");
        _miLang.Text = Loc.T("tray.language");
        _miSettings.Text = Loc.T("tray.settings");
        _miExit.Text = Loc.T("tray.exit");
        _miLangEn.Text = "English";
        _miLangZh.Text = "繁體中文";
    }

    private void SyncMenu()
    {
        RebuildMenuTexts();
        _miPanel.Checked = _panel is { Visible: true };
        _miAuto.Checked = Autostart.IsEnabled();
        _miLangEn.Checked = !Loc.IsZh;
        _miLangZh.Checked = Loc.IsZh;
    }

    private void SetLanguage(string language)
    {
        Loc.SetLanguage(language);
        Loc.RefreshOpenForms();
        SyncMenu();
        _settings.Language = language;
        _settings.Save();
    }

    private void ToggleAutostart()
    {
        bool next = !Autostart.IsEnabled();
        Autostart.SetEnabled(next);
        _settings.AutoStart = next;
        _settings.Save();
        SyncMenu();
    }

    private void TogglePanel()
    {
        if (_panel is { Visible: true })
        {
            HidePanel();
        }
        else
        {
            ShowPanel();
        }

        SyncMenu();
    }

    // ------------------------------------------------------------------ windows

    public void ShowMain()
    {
        if (_main.IsDisposed) return;
        if (!_main.Visible) _main.Show();
        if (_main.WindowState == FormWindowState.Minimized) _main.WindowState = FormWindowState.Normal;
        _main.Activate();
        _main.BringToFront();
    }

    public void ShowStats()
    {
        if (_stats is null || _stats.IsDisposed)
        {
            _stats = new StatsForm(_db, _sampler);
        }

        if (!_stats.Visible) _stats.Show(this._main);
        if (_stats.WindowState == FormWindowState.Minimized) _stats.WindowState = FormWindowState.Normal;
        _stats.Activate();
    }

    public void ShowSettings()
    {
        if (_settingsForm is { Visible: true })
        {
            _settingsForm.Activate();
            return;
        }

        _settingsForm?.Dispose();
        _settingsForm = new SettingsForm(_settings, _db);
        _settingsForm.Saved += OnSettingsSaved;
        _settingsForm.FormClosed += (_, _) => { _settingsForm = null; };
        _settingsForm.ShowDialog(_main);
    }

    private void OnSettingsSaved()
    {
        if (_settings.FloatingPanel) ShowPanel();
        else HidePanel();

        try
        {
            _sampler.ResetBaseline();
            _sampler.PruneNow();
        }
        catch
        {
            // pruning is best-effort
        }

        SyncMenu();
    }

    private void CreatePanel()
    {
        _panel = new FloatingPanel(_settings, _db);
        _panel.OpenMain += ShowMain;
        _panel.OpenStats += ShowStats;
        _panel.OpenSettings += ShowSettings;
        _panel.ToggleSelf += HidePanel;
        _panel.ExitApp += Exit;
        _panel.FormClosed += (_, _) =>
        {
            _settings.PanelX = _panel?.Location.X ?? _settings.PanelX;
            _settings.PanelY = _panel?.Location.Y ?? _settings.PanelY;
            _panel = null;
            SyncMenu();
        };
    }

    private void ShowPanel()
    {
        if (_panel is null || _panel.IsDisposed) CreatePanel();
        if (_panel is null) return;

        _panel.Show();
        _panel.BringToFront();
        _settings.FloatingPanel = true;
        _settings.Save();
        SyncMenu();
    }

    private void HidePanel()
    {
        if (_panel is null) return;
        _settings.PanelX = _panel.Location.X;
        _settings.PanelY = _panel.Location.Y;
        _panel.Hide();
        _settings.FloatingPanel = false;
        _settings.Save();
        SyncMenu();
    }

    // ------------------------------------------------------------------ shutdown

    private void Exit()
    {
        if (_stopping) return;
        _stopping = true;

        try { _timer.Stop(); } catch { /* ignore */ }
        try { _settings.Save(); } catch { /* ignore */ }

        if (_panel is { Visible: true })
        {
            _settings.PanelX = _panel.Location.X;
            _settings.PanelY = _panel.Location.Y;
        }

        _tray.Visible = false;
        ExitThread();
    }

    protected override void ExitThreadCore()
    {
        Loc.LanguageChanged -= OnLanguageChanged;

        try { _timer.Dispose(); } catch { /* ignore */ }
        try { _tray.Dispose(); } catch { /* ignore */ }
        try { _menu.Dispose(); } catch { /* ignore */ }

        try { _stats?.Dispose(); } catch { /* ignore */ }
        try { _settingsForm?.Dispose(); } catch { /* ignore */ }
        try { _panel?.Dispose(); } catch { /* ignore */ }
        try { _main.Dispose(); } catch { /* ignore */ }

        base.ExitThreadCore();
    }
}
