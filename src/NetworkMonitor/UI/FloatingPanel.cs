using NetworkMonitor.Core;
using NetworkMonitor.UI.Controls;

namespace NetworkMonitor.UI;

/// <summary>
/// Small always-on-top widget showing the aggregate real-time rates and a live sparkline.
/// Borderless, draggable, double click opens the main window, right click shows a menu.
/// </summary>
public sealed class FloatingPanel : Form, ILocalizable
{
    private const int PanelWidth = 244;
    private const int PanelHeight = 118;

    private readonly AppSettings _settings;
    private readonly TrafficDatabase _db;
    private readonly SparklineControl _spark = new() { Capacity = 90 };
    private readonly Label _downValue = new();
    private readonly Label _upValue = new();
    private readonly Label _todayCaption = new();
    private readonly Label _todayDown = new();
    private readonly Label _todayUp = new();
    private readonly Label _caption = new();
    private readonly Label _close = new();
    private readonly ContextMenuStrip _menu = new();

    private long _todayRx;
    private long _todayTx;
    private DateTime _lastTodayQueryUtc = DateTime.MinValue;

    public event Action? OpenMain;
    public event Action? OpenStats;
    public event Action? OpenSettings;
    public event Action? ToggleSelf;
    public event Action? ExitApp;

    public FloatingPanel(AppSettings settings, TrafficDatabase db)
    {
        _settings = settings;
        _db = db;

        SuspendLayout();

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(PanelWidth, PanelHeight);
        BackColor = Color.FromArgb(19, 27, 38);
        Opacity = 0.9; // semi-transparent overlay
        KeyPreview = false;

        int x = _settings.PanelX ?? Screen.PrimaryScreen?.WorkingArea.Right - PanelWidth - 24 ?? 400;
        int y = _settings.PanelY ?? Screen.PrimaryScreen?.WorkingArea.Bottom - PanelHeight - 64 ?? 300;
        Location = new Point(x, y);

        _caption.Text = Loc.T("panel.title");
        _caption.SetBounds(12, 6, 130, 14);
        _caption.ForeColor = Color.FromArgb(148, 163, 184);
        _caption.Font = new Font("Segoe UI", 8f);
        _caption.BackColor = Color.Transparent;

        _close.Text = "×";
        _close.SetBounds(PanelWidth - 28, 4, 24, 22);
        _close.ForeColor = Color.FromArgb(148, 163, 184);
        _close.Font = new Font("Segoe UI", 12f);
        _close.TextAlign = ContentAlignment.MiddleCenter;
        _close.BackColor = Color.Transparent;
        _close.Cursor = Cursors.Hand;
        _close.Click += (_, _) => ToggleSelf?.Invoke();
        _close.MouseEnter += (_, _) => _close.ForeColor = Color.White;
        _close.MouseLeave += (_, _) => _close.ForeColor = Color.FromArgb(148, 163, 184);

        _downValue.Text = "↓ 0 B/s";
        _downValue.SetBounds(12, 19, PanelWidth - 30, 27);
        _downValue.Font = new Font("Segoe UI Semibold", 14f, FontStyle.Bold);
        _downValue.ForeColor = Color.FromArgb(77, 182, 255);
        _downValue.BackColor = Color.Transparent;
        _downValue.TextAlign = ContentAlignment.MiddleLeft;

        _upValue.Text = "↑ 0 B/s";
        _upValue.SetBounds(12, 44, PanelWidth - 30, 23);
        _upValue.Font = new Font("Segoe UI Semibold", 13f, FontStyle.Bold);
        _upValue.ForeColor = Color.FromArgb(255, 176, 92);
        _upValue.BackColor = Color.Transparent;
        _upValue.TextAlign = ContentAlignment.MiddleLeft;

        // today's cumulative totals - a compact single row above the sparkline
        _todayCaption.Text = Loc.T("main.today");
        _todayCaption.SetBounds(12, 69, 40, 16);
        _todayCaption.ForeColor = Color.FromArgb(148, 163, 184);
        _todayCaption.Font = new Font("Segoe UI", 8.5f);
        _todayCaption.BackColor = Color.Transparent;

        _todayDown.Text = "↓ 0 B";
        _todayDown.SetBounds(54, 69, 82, 16);
        _todayDown.ForeColor = Color.FromArgb(125, 185, 235);
        _todayDown.Font = new Font("Segoe UI", 8.5f);
        _todayDown.BackColor = Color.Transparent;
        _todayDown.TextAlign = ContentAlignment.MiddleLeft;

        _todayUp.Text = "↑ 0 B";
        _todayUp.SetBounds(140, 69, 92, 16);
        _todayUp.ForeColor = Color.FromArgb(245, 190, 140);
        _todayUp.Font = new Font("Segoe UI", 8.5f);
        _todayUp.BackColor = Color.Transparent;
        _todayUp.TextAlign = ContentAlignment.MiddleLeft;

        _spark.SetBounds(8, 87, PanelWidth - 16, 25);
        _spark.DownColor = Color.FromArgb(77, 182, 255);
        _spark.UpColor = Color.FromArgb(255, 176, 92);

        Controls.Add(_caption);
        Controls.Add(_close);
        Controls.Add(_downValue);
        Controls.Add(_upValue);
        Controls.Add(_todayCaption);
        Controls.Add(_todayDown);
        Controls.Add(_todayUp);
        Controls.Add(_spark);

        BuildMenu();
        _menu.Opening += (_, _) => ApplyMenuLocalization();

        ToolTip = new ToolTip { AutoPopDelay = 8000, InitialDelay = 400 };
        ToolTip.SetToolTip(this, Loc.T("panel.tip"));

        ApplyLocalization();
        UpdateRegion();
        ResumeLayout(false);
    }

    private ToolTip ToolTip { get; }

    private void BuildMenu()
    {
        _menu.Items.Clear();
        _menu.Items.Add(CreateItem(Loc.T("tray.show"), (_, _) => OpenMain?.Invoke()));
        _menu.Items.Add(CreateItem(Loc.T("tray.stats"), (_, _) => OpenStats?.Invoke()));
        _menu.Items.Add(CreateItem(Loc.T("tray.settings"), (_, _) => OpenSettings?.Invoke()));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(CreateItem(Loc.T("tray.panel"), null, (_, _) => ToggleSelf?.Invoke()));
        _menu.Items.Add(CreateItem(Loc.T("tray.exit"), null, (_, _) => ExitApp?.Invoke()));
    }

    private static ToolStripMenuItem CreateItem(string text, EventHandler? onClick, EventHandler? checkedHandler = null)
    {
        var item = new ToolStripMenuItem(text);
        if (onClick is not null) item.Click += onClick;
        if (checkedHandler is not null) item.Click += checkedHandler;
        return item;
    }

    private void ApplyMenuLocalization()
    {
        if (_menu.Items.Count < 6) return;
        _menu.Items[0].Text = Loc.T("tray.show");
        _menu.Items[1].Text = Loc.T("tray.stats");
        _menu.Items[2].Text = Loc.T("tray.settings");
        _menu.Items[4].Text = Loc.T("tray.panel");
        _menu.Items[5].Text = Loc.T("tray.exit");
    }

    public void ApplyLocalization()
    {
        _caption.Text = Loc.T("panel.title");
        _todayCaption.Text = Loc.T("main.today");
        ToolTip.SetToolTip(this, Loc.T("panel.tip"));
        BuildMenu();
        Invalidate();
    }

    public void PushSnapshot(Snapshot snapshot)
    {
        if (!Visible || IsDisposed) return;
        _downValue.Text = "↓ " + FormatUtil.Rate(snapshot.DownRate);
        _upValue.Text = "↑ " + FormatUtil.Rate(snapshot.UpRate);
        _todayDown.Text = "↓ " + FormatUtil.Bytes(_todayRx);
        _todayUp.Text = "↑ " + FormatUtil.Bytes(_todayTx);
        _spark.AddSample(snapshot.DownRate, snapshot.UpRate);

        // today's totals come from SQLite once a minute (queried off the UI thread)
        if ((DateTime.UtcNow - _lastTodayQueryUtc).TotalSeconds >= 60)
        {
            _lastTodayQueryUtc = DateTime.UtcNow;
            int day = DateUtil.DayKey(DateTime.Today);
            Task.Run(() =>
            {
                long rx = 0, tx = 0;
                foreach (var kv in _db.TotalsPerInterface(day))
                {
                    rx += kv.Value.Rx;
                    tx += kv.Value.Tx;
                }

                try
                {
                    if (!IsDisposed) BeginInvoke(() => { _todayRx = rx; _todayTx = tx; });
                }
                catch
                {
                    // window closed while the query was running
                }
            });
        }
    }

    private void UpdateRegion()
    {
        int r = 10;
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        var rect = new Rectangle(0, 0, Width, Height);
        float d = r * 2f;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        Region?.Dispose();
        Region = new Region(path);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (Width != PanelWidth || Height != PanelHeight)
            Size = new Size(PanelWidth, PanelHeight);
        UpdateRegion();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            Capture = false;
            ReleaseCapture();
            SendMessage(Handle, 0xA1, 2, IntPtr.Zero); // WM_NCHITTEST / HTCAPTION
        }
        else if (e.Button == MouseButtons.Right)
        {
            _menu.Show(this, e.Location);
        }
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button == MouseButtons.Left) OpenMain?.Invoke();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _settings.PanelX = Location.X;
        _settings.PanelY = Location.Y;
        base.OnFormClosed(e);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ReleaseCapture();
}
