using NetworkMonitor.Core;
using NetworkMonitor.UI.Controls;

namespace NetworkMonitor.UI;

/// <summary>
/// Small always-on-top widget showing the aggregate real-time rates, a live sparkline and -
/// once processes are being monitored - one compact block per process with the same fields
/// (live rates + today's totals). Borderless, draggable, double click opens the main
/// window, right click shows a menu. The base size stays exactly as before; the panel only
/// grows while monitored processes exist (max 5 blocks).
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
    private readonly ProcStrip _strip = new();

    private long _todayRx;
    private long _todayTx;
    private DateTime _lastTodayQueryUtc = DateTime.MinValue;
    private int _desiredHeight = PanelHeight;
    private int _stripHeight;

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

        _strip.SetBounds(0, PanelHeight, PanelWidth, 0);
        _strip.BackColor = Color.FromArgb(19, 27, 38);

        Controls.Add(_caption);
        Controls.Add(_close);
        Controls.Add(_downValue);
        Controls.Add(_upValue);
        Controls.Add(_todayCaption);
        Controls.Add(_todayDown);
        Controls.Add(_todayUp);
        Controls.Add(_spark);
        Controls.Add(_strip);

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
        _strip.Invalidate();
    }

    public void PushSnapshot(Snapshot snapshot)
    {
        if (!Visible || IsDisposed) return;
        _downValue.Text = "↓ " + FormatUtil.Rate(snapshot.DownRate);
        _upValue.Text = "↑ " + FormatUtil.Rate(snapshot.UpRate);
        _todayDown.Text = "↓ " + FormatUtil.Bytes(_todayRx);
        _todayUp.Text = "↑ " + FormatUtil.Bytes(_todayTx);
        _spark.AddSample(snapshot.DownRate, snapshot.UpRate);

        _strip.SetData(snapshot.Processes, snapshot.ProcState, _procToday);

        int stripHeight = _strip.ContentHeight;
        if (stripHeight != _stripHeight)
        {
            _stripHeight = stripHeight;
            _desiredHeight = PanelHeight + stripHeight;
            _strip.SetBounds(0, PanelHeight, PanelWidth, stripHeight);
            Height = _desiredHeight; // OnResize re-applies region
        }

        // today's totals (global + per process) come from SQLite once a minute, off the UI thread
        if ((DateTime.UtcNow - _lastTodayQueryUtc).TotalSeconds >= 60)
        {
            _lastTodayQueryUtc = DateTime.UtcNow;
            int day = DateUtil.DayKey(DateTime.Today);
            Task.Run(() =>
            {
                long rx = 0, tx = 0;
                var perProc = new Dictionary<string, DayTotal>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in _db.TotalsPerInterface(day))
                {
                    if (TrafficKeys.IsProcess(kv.Key))
                        perProc[kv.Key[TrafficKeys.ProcessPrefix.Length..]] = kv.Value;
                    else
                    {
                        rx += kv.Value.Rx;
                        tx += kv.Value.Tx;
                    }
                }

                try
                {
                    if (!IsDisposed)
                        BeginInvoke(() =>
                        {
                            _todayRx = rx;
                            _todayTx = tx;
                            _procToday = perProc;
                        });
                }
                catch
                {
                    // window closed while the query was running
                }
            });
        }
    }

    private Dictionary<string, DayTotal> _procToday = new(StringComparer.OrdinalIgnoreCase);

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
        if (Width != PanelWidth || Height != _desiredHeight)
            Size = new Size(PanelWidth, _desiredHeight);
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

    /// <summary>
    /// The stacked process blocks below the sparkline: one row per monitored process with
    /// live down/up rates and today's totals; a hint row when ETW could not start.
    /// Double-buffered child control so the 1 Hz updates stay flicker-free.
    /// </summary>
    private sealed class ProcStrip : Control
    {
        private const int HintHeight = 18;
        private const int RowHeight = 28;

        private static readonly Color DownColor = Color.FromArgb(77, 182, 255);
        private static readonly Color UpColor = Color.FromArgb(255, 176, 92);
        private static readonly Color MutedColor = Color.FromArgb(148, 163, 184);
        private static readonly Color HintColor = Color.FromArgb(240, 130, 90);

        /// <summary>Fixed width of one value column ("↓ 153 MB/s" fits); keeps rows inside the panel.</summary>
        private const int ValueCol = 66;

        private readonly Font _nameFont = new("Segoe UI Semibold", 8.5f, FontStyle.Bold);
        private readonly Font _valueFont = new("Segoe UI Semibold", 8.5f, FontStyle.Bold);
        private readonly Font _smallFont = new("Segoe UI", 8f);

        private IReadOnlyList<ProcInfo> _items = Array.Empty<ProcInfo>();
        private ProcessMonitorState _state = ProcessMonitorState.Stopped;
        private IReadOnlyDictionary<string, DayTotal> _today = new Dictionary<string, DayTotal>();
        private string _renderKey = "";

        public ProcStrip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.FromArgb(19, 27, 38);
        }

        public int ContentHeight =>
            _items.Count == 0
                ? 0
                : _items.Count * RowHeight + (NeedsHint ? HintHeight : 0);

        private bool NeedsHint => _items.Count > 0 && _state != ProcessMonitorState.Running;

        public void SetData(IReadOnlyList<ProcInfo> items, ProcessMonitorState state,
            IReadOnlyDictionary<string, DayTotal> today)
        {
            _items = items;
            _state = state;
            _today = today;

            var sb = new System.Text.StringBuilder(48);
            sb.Append(_state).Append('|');
            foreach (var p in _items)
            {
                today.TryGetValue(p.Name, out var t);
                sb.Append(p.Name).Append(':').Append(p.RxRate).Append('/').Append(p.TxRate)
                  .Append(';').Append(t.Rx).Append('/').Append(t.Tx).Append(';');
            }

            string key = sb.ToString();
            if (key != _renderKey && _items.Count > 0)
            {
                _renderKey = key;
                Invalidate();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _nameFont.Dispose();
                _valueFont.Dispose();
                _smallFont.Dispose();
            }

            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            if (_items.Count == 0) return;

            int y = 2;
            if (NeedsHint)
            {
                var hintRect = new Rectangle(12, y, Width - 20, HintHeight - 3);
                TextRenderer.DrawText(g, "⚠ " + Loc.T("proc.adminNeeded"), _smallFont, hintRect, HintColor,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                y += HintHeight;
            }

            foreach (var p in _items)
            {
                DrawRow(g, p, y);
                y += RowHeight;
            }
        }

        private void DrawRow(Graphics g, ProcInfo p, int y)
        {
            int upRight = Width - 8;        // ↑ value right edge
            int downRight = upRight - ValueCol;

            string name = p.Name + ".exe";
            int nameMax = Math.Max(30, downRight - ValueCol - 6 - 12);
            var nameRect = new Rectangle(12, y + 1, nameMax, 14);
            TextRenderer.DrawText(g, name, _nameFont, nameRect, Color.White,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

            // line 2 holds the caption at x=12; line 1 gives the name the full first column
            DrawCell(g, "↓ " + FormatUtil.Rate(p.RxRate), downRight, _valueFont, y + 1, 14, DownColor);
            DrawCell(g, "↑ " + FormatUtil.Rate(p.TxRate), upRight, _valueFont, y + 1, 14, UpColor);

            _today.TryGetValue(p.Name, out var today);
            var capRect = new Rectangle(12, y + 15, 42, 12);
            TextRenderer.DrawText(g, Loc.T("main.today"), _smallFont, capRect, MutedColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);

            DrawCell(g, "↓ " + FormatUtil.Bytes(today.Rx), downRight, _smallFont, y + 15, 12, DownColor);
            DrawCell(g, "↑ " + FormatUtil.Bytes(today.Tx), upRight, _smallFont, y + 15, 12, UpColor);
        }

        /// <summary>Right-aligned value that always stays inside the panel bounds.</summary>
        private void DrawCell(Graphics g, string text, int rightEdge, Font font, int y, int height, Color color)
        {
            var rect = new Rectangle(rightEdge - ValueCol, y - 1, ValueCol, height + 2);
            TextRenderer.DrawText(g, text, font, rect, color,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }
}
