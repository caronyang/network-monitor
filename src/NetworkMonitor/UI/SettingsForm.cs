using NetworkMonitor.Core;

namespace NetworkMonitor.UI;

/// <summary>Settings dialog. Changes are applied immediately after the user presses Save.</summary>
public sealed class SettingsForm : Form, ILocalizable
{
    private readonly AppSettings _settings;
    private readonly TrafficDatabase _db;

    private readonly TabControl _tabs = new();
    private readonly TabPage _tabGeneral = new();
    private readonly TabPage _tabMonitor = new();
    private readonly TabPage _tabData = new();

    private readonly Label _lblLanguage = new();
    private readonly ComboBox _cbLanguage = new();
    private readonly CheckBox _chkAutoStart = new();
    private readonly CheckBox _chkPanel = new();
    private readonly CheckBox _chkStartMin = new();
    private readonly CheckBox _chkMinTray = new();

    private readonly Label _lblInterval = new();
    private readonly NumericUpDown _numInterval = new();
    private readonly Label _lblIntervalHint = new();
    private readonly Label _lblRetention = new();
    private readonly NumericUpDown _numRetention = new();
    private readonly Label _lblRetentionHint = new();
    private readonly CheckBox _chkLoopback = new();
    private readonly CheckBox _chkDisconnected = new();
    private readonly CheckBox _chkFilter = new();

    private readonly Label _lblDbInfo = new();
    private readonly Label _lblResolution = new();
    private readonly Button _btnOpenFolder = new();
    private readonly Button _btnClear = new();

    private readonly Button _btnSave = new();
    private readonly Button _btnCancel = new();

    private readonly FlowLayoutPanel _bottom = new();

    public event Action? Saved;

    public SettingsForm(AppSettings settings, TrafficDatabase db)
    {
        _settings = settings;
        _db = db;

        SuspendLayout();

        Name = "SettingsForm";
        Text = Loc.T("settings.title");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(520, 470);
        BackColor = Color.White;
        ShowInTaskbar = false;

        // ---- tabs ----
        _tabs.Dock = DockStyle.Fill;
        _tabs.Padding = new Point(14, 6);
        _tabs.Font = new Font("Segoe UI", 9f);

        _tabGeneral.BackColor = Color.White;
        _tabMonitor.BackColor = Color.White;
        _tabData.BackColor = Color.White;
        _tabGeneral.Text = Loc.T("settings.general");
        _tabMonitor.Text = Loc.T("settings.monitor");
        _tabData.Text = Loc.T("settings.data");

        BuildGeneralTab();
        BuildMonitorTab();
        BuildDataTab();

        _tabs.TabPages.Add(_tabGeneral);
        _tabs.TabPages.Add(_tabMonitor);
        _tabs.TabPages.Add(_tabData);

        // ---- bottom buttons ----
        _bottom.Dock = DockStyle.Bottom;
        _bottom.AutoSize = true; // hugs the buttons so the bottom padding is guaranteed
        _bottom.FlowDirection = FlowDirection.RightToLeft;
        _bottom.WrapContents = false;
        _bottom.Padding = new Padding(0, 8, 14, 12);

        StyleButton(_btnCancel, Loc.T("common.cancel"), (_, _) => { Hide(); Dispose(); });
        StyleButton(_btnSave, Loc.T("common.save"), (_, _) => SaveAndClose());
        _btnSave.BackColor = Color.FromArgb(30, 136, 229);
        _btnSave.ForeColor = Color.White;
        _btnSave.FlatAppearance.BorderColor = Color.FromArgb(30, 136, 229);

        _bottom.Controls.Add(_btnCancel);
        _bottom.Controls.Add(_btnSave);

        Controls.Add(_tabs);
        Controls.Add(_bottom);

        LoadFromSettings();
        ApplyLocalization();
        RefreshDbInfo();

        Load += (_, _) => RefreshDbInfo();

        ResumeLayout(true);
    }

    private void BuildGeneralTab()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(16, 18, 16, 12),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _lblLanguage.AutoSize = true;
        _lblLanguage.Anchor = AnchorStyles.Left;
        _cbLanguage.Width = 200;
        _cbLanguage.DropDownStyle = ComboBoxStyle.DropDownList;
        _cbLanguage.Items.AddRange(new object[] { "English", "繁體中文" });
        _cbLanguage.SelectedIndexChanged += (_, _) =>
        {
            Loc.SetLanguage(_cbLanguage.SelectedIndex == 1 ? Loc.Chinese : Loc.English);
            Loc.RefreshOpenForms();
        };

        panel.Controls.Add(_lblLanguage, 0, 0);
        panel.Controls.Add(_cbLanguage, 1, 0);
        panel.Controls.Add(CheckRow(_chkAutoStart, 1), 0, 1);
        panel.Controls.Add(CheckRow(_chkPanel, 1), 0, 2);
        panel.Controls.Add(CheckRow(_chkStartMin, 1), 0, 3);
        panel.Controls.Add(CheckRow(_chkMinTray, 1), 0, 4);
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _tabGeneral.Controls.Add(panel);
    }

    private Control CheckRow(CheckBox box, int colspan)
    {
        box.AutoSize = true;
        box.Margin = new Padding(3, 8, 3, 4);
        return box;
    }

    private void BuildMonitorTab()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(16, 18, 16, 12),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _lblInterval.AutoSize = true;
        _lblInterval.Anchor = AnchorStyles.Left;
        _numInterval.Minimum = 200;
        _numInterval.Maximum = 60000;
        _numInterval.Increment = 100;
        _numInterval.Width = 140;
        _numInterval.TextAlign = HorizontalAlignment.Right;

        _lblIntervalHint.AutoSize = true;
        _lblIntervalHint.ForeColor = Color.FromArgb(130, 142, 156);
        _lblIntervalHint.Font = new Font("Segoe UI", 8.25f);
        _lblIntervalHint.Margin = new Padding(3, 2, 3, 10);

        _lblRetention.AutoSize = true;
        _lblRetention.Anchor = AnchorStyles.Left;
        _numRetention.Minimum = 1;
        _numRetention.Maximum = 3650;
        _numRetention.Width = 140;
        _numRetention.TextAlign = HorizontalAlignment.Right;

        _lblRetentionHint.AutoSize = true;
        _lblRetentionHint.ForeColor = Color.FromArgb(130, 142, 156);
        _lblRetentionHint.Font = new Font("Segoe UI", 8.25f);
        _lblRetentionHint.Margin = new Padding(3, 2, 3, 10);

        panel.Controls.Add(_lblInterval, 0, 0);
        panel.Controls.Add(_numInterval, 1, 0);
        panel.Controls.Add(_lblIntervalHint, 1, 1);
        panel.Controls.Add(_lblRetention, 0, 2);
        panel.Controls.Add(_numRetention, 1, 2);
        panel.Controls.Add(_lblRetentionHint, 1, 3);
        panel.Controls.Add(CheckRow(_chkLoopback, 1), 0, 4);
        panel.Controls.Add(CheckRow(_chkDisconnected, 1), 0, 5);
        panel.Controls.Add(CheckRow(_chkFilter, 1), 0, 6);

        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _tabMonitor.Controls.Add(panel);
    }

    private void BuildDataTab()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(16, 18, 16, 12),
        };

        _lblDbInfo.AutoSize = true;
        _lblDbInfo.ForeColor = Color.FromArgb(45, 62, 80);
        _lblDbInfo.Margin = new Padding(3, 4, 3, 14);

        _lblResolution.AutoSize = true;
        _lblResolution.ForeColor = Color.FromArgb(130, 142, 156);
        _lblResolution.Font = new Font("Segoe UI", 8.25f);
        _lblResolution.Margin = new Padding(3, 0, 3, 18);

        StyleButton(_btnOpenFolder, Loc.T("settings.openFolder"), (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = AppSettings.DataFolder,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Loc.T("common.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        });
        _btnOpenFolder.Margin = new Padding(3, 6, 3, 10);

        StyleButton(_btnClear, Loc.T("settings.clear"), (_, _) => ClearData());
        _btnClear.ForeColor = Color.FromArgb(200, 60, 60);
        _btnClear.FlatAppearance.BorderColor = Color.FromArgb(232, 190, 190);
        _btnClear.Margin = new Padding(3, 6, 3, 10);

        panel.Controls.Add(_lblDbInfo);
        panel.Controls.Add(_lblResolution);
        panel.Controls.Add(_btnOpenFolder);
        panel.Controls.Add(_btnClear);

        _tabData.Controls.Add(panel);
    }

    private void ClearData()
    {
        var confirm = MessageBox.Show(this, Loc.T("settings.clearAsk"), Loc.T("settings.title"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes) return;

        try
        {
            _db.Clear();
            RefreshDbInfo();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Loc.T("common.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RefreshDbInfo()
    {
        long size = _db.FileSizeBytes;
        long rows = _db.Count();
        _lblDbInfo.Text = Loc.T("settings.dbInfo", FormatUtil.Bytes(size), rows.ToString("N0"));
    }

    private void LoadFromSettings()
    {
        _cbLanguage.SelectedIndex = _settings.Language == Loc.Chinese ? 1 : 0;
        _chkAutoStart.Checked = Autostart.IsEnabled();
        _chkPanel.Checked = _settings.FloatingPanel;
        _chkStartMin.Checked = _settings.StartMinimized;
        _chkMinTray.Checked = _settings.MinimizeToTray;
        _numInterval.Value = Math.Clamp(_settings.SampleIntervalMs, 200, 60000);
        _numRetention.Value = Math.Clamp(_settings.RetentionDays, 1, 3650);
        _chkLoopback.Checked = _settings.IncludeLoopback;
        _chkDisconnected.Checked = _settings.IncludeDisconnected;
        _chkFilter.Checked = _settings.ShowFilterAdapters;
    }

    private void SaveAndClose()
    {
        _settings.Language = Loc.Language;
        _settings.AutoStart = _chkAutoStart.Checked;
        _settings.FloatingPanel = _chkPanel.Checked;
        _settings.StartMinimized = _chkStartMin.Checked;
        _settings.MinimizeToTray = _chkMinTray.Checked;
        _settings.SampleIntervalMs = (int)_numInterval.Value;
        _settings.RetentionDays = (int)_numRetention.Value;
        _settings.IncludeLoopback = _chkLoopback.Checked;
        _settings.IncludeDisconnected = _chkDisconnected.Checked;
        _settings.ShowFilterAdapters = _chkFilter.Checked;
        _settings.Normalize();
        _settings.Save();

        Autostart.SetEnabled(_settings.AutoStart);

        try { Saved?.Invoke(); }
        catch { /* listeners are best-effort; never block closing the dialog */ }
        Hide();
        Dispose();
    }

    public void ApplyLocalization()
    {
        Text = Loc.T("settings.title");
        _tabGeneral.Text = Loc.T("settings.general");
        _tabMonitor.Text = Loc.T("settings.monitor");
        _tabData.Text = Loc.T("settings.data");

        _lblLanguage.Text = Loc.T("settings.language");
        _chkAutoStart.Text = Loc.T("settings.autoStart");
        _chkPanel.Text = Loc.T("settings.panel");
        _chkStartMin.Text = Loc.T("settings.startMin");
        _chkMinTray.Text = Loc.T("settings.minTray");

        _lblInterval.Text = Loc.T("settings.interval");
        _lblIntervalHint.Text = Loc.T("settings.intervalRange");
        _lblRetention.Text = Loc.T("settings.retention");
        _lblRetentionHint.Text = Loc.T("settings.retentionRange");
        _chkLoopback.Text = Loc.T("settings.loopback");
        _chkDisconnected.Text = Loc.T("settings.down");
        _chkFilter.Text = Loc.T("settings.filter");

        _btnOpenFolder.Text = Loc.T("settings.openFolder");
        _btnClear.Text = Loc.T("settings.clear");
        _btnSave.Text = Loc.T("common.save");
        _btnCancel.Text = Loc.T("common.cancel");
        RefreshDbInfo();
    }

    private static void StyleButton(Button b, string text, EventHandler onClick)
    {
        b.Text = text;
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderColor = Color.FromArgb(214, 222, 232);
        b.BackColor = Color.White;
        b.ForeColor = Color.FromArgb(45, 62, 80);
        b.Font = new Font("Segoe UI", 9f);
        b.AutoSize = true;
        b.Padding = new Padding(12, 5, 12, 5);
        b.Cursor = Cursors.Hand;
        b.Click += onClick;
    }
}
