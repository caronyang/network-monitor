using NetworkMonitor.Core;
using NetworkMonitor.UI.Controls;

namespace NetworkMonitor.UI;

/// <summary>
/// Main dashboard: summary band + owner-drawn interface grid.
/// All data is pushed into the form by <see cref="TrayApplicationContext"/>'s 1 Hz timer.
/// </summary>
public sealed class MainForm : Form, ILocalizable
{
    private readonly AppSettings _settings;
    private readonly TrafficDatabase _db;

    private readonly Panel _toolbar = new();
    private readonly Label _title = new();
    private readonly Button _btnStats = new();
    private readonly Button _btnSettings = new();

    private readonly SummaryBand _summary = new();
    private readonly InterfaceGridControl _grid = new();

    private IReadOnlyDictionary<string, DayTotal> _today = new Dictionary<string, DayTotal>();
    private bool _todayQueryRunning;
    private DateTime _lastTodayQuery = DateTime.MinValue;

    public event Action? RequestStats;
    public event Action? RequestSettings;

    public MainForm(AppSettings settings, TrafficDatabase db)
    {
        _settings = settings;
        _db = db;

        SuspendLayout();

        Name = "MainForm";
        Text = Loc.T("main.title");
        MinimumSize = new Size(720, 480);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.White;
        ShowInTaskbar = true;

        if (_settings.MainWidth is int w && _settings.MainHeight is int h && w >= 720 && h >= 480)
            Size = new Size(w, h);

        // ---- toolbar ----
        _toolbar.Dock = DockStyle.Top;
        _toolbar.Height = 46;
        _toolbar.BackColor = Color.White;
        _toolbar.Padding = new Padding(12, 7, 12, 5);

        _title.Dock = DockStyle.Left;
        _title.AutoSize = true;
        _title.Text = Loc.T("main.title");
        _title.Font = new Font("Segoe UI Semibold", 12.5f, FontStyle.Bold);
        _title.ForeColor = Color.FromArgb(28, 40, 56);
        _title.TextAlign = ContentAlignment.MiddleLeft;
        _title.Padding = new Padding(4, 0, 0, 0);

        var right = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
            Padding = new Padding(0, 2, 0, 0),
        };

        ConfigureButton(_btnSettings, (_, _) => RequestSettings?.Invoke());
        ConfigureButton(_btnStats, (_, _) => RequestStats?.Invoke());

        // add in reverse order because of RightToLeft flow
        right.Controls.Add(_btnSettings);
        right.Controls.Add(_btnStats);

        _toolbar.Controls.Add(right);
        _toolbar.Controls.Add(_title); // title last -> left side

        // ---- summary band ----
        _summary.Dock = DockStyle.Top;
        _summary.Height = 96;

        // ---- grid ----
        _grid.Dock = DockStyle.Fill;

        Controls.Add(_grid);
        Controls.Add(_summary);
        Controls.Add(_toolbar);

        ApplyLocalization();
        ResumeLayout(false);
    }

    private static void ConfigureButton(Button b, EventHandler onClick)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.BorderColor = Color.FromArgb(214, 222, 232);
        b.BackColor = Color.White;
        b.ForeColor = Color.FromArgb(45, 62, 80);
        b.Font = new Font("Segoe UI", 9f);
        b.AutoSize = true;
        b.Padding = new Padding(10, 0, 10, 0);
        b.Margin = new Padding(6, 0, 0, 0);
        b.Cursor = Cursors.Hand;
        b.Click += onClick;
    }

    public void ApplyLocalization()
    {
        Text = Loc.T("main.title");
        _title.Text = Loc.T("main.title");
        _btnStats.Text = Loc.T("main.stats");
        _btnSettings.Text = Loc.T("main.settings");
        _summary.Invalidate();
        _grid.Invalidate();
    }

    /// <summary>Called by the tray context at 1 Hz.</summary>
    public void PushSnapshot(Snapshot snapshot)
    {
        if (!Visible || IsDisposed) return;

        _summary.SetData(snapshot, SumToday());
        _grid.SetData(snapshot.Items, _today);
        RefreshTodayIfNeeded(snapshot);
    }

    private DayTotal SumToday()
    {
        long rx = 0, tx = 0;
        foreach (var v in _today.Values)
        {
            rx += v.Rx;
            tx += v.Tx;
        }

        return new DayTotal(rx, tx);
    }

    private void RefreshTodayIfNeeded(Snapshot snapshot)
    {
        if (_todayQueryRunning) return;
        if ((DateTime.UtcNow - _lastTodayQuery).TotalSeconds < 45) return;
        if (!snapshot.Valid) return;

        _todayQueryRunning = true;
        _lastTodayQuery = DateTime.UtcNow;
        int day = DateUtil.DayKey(DateTime.Today);
        Task.Run(() =>
        {
            var totals = _db.TotalsPerInterface(day);
            try
            {
                BeginInvoke(() =>
                {
                    _today = totals;
                    _todayQueryRunning = false;
                    if (Visible && !IsDisposed)
                    {
                        _summary.SetData(_summary.Snapshot, SumToday());
                        _grid.SetData(_grid.Items, _today);
                    }
                });
            }
            catch
            {
                _todayQueryRunning = false;
            }
        });
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && _settings.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState != FormWindowState.Minimized)
        {
            _settings.MainWidth = Width;
            _settings.MainHeight = Height;
        }
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        _settings.Save(); // cheap enough, keeps window size persisted
    }

    /// <summary>Dark summary band with the aggregate rates and totals.</summary>
    private sealed class SummaryBand : Control
    {
        private static readonly Color BandBack = Color.FromArgb(23, 32, 45);
        private static readonly Color BandDown = Color.FromArgb(77, 182, 255);
        private static readonly Color BandUp = Color.FromArgb(255, 176, 92);
        private static readonly Color Muted = Color.FromArgb(148, 163, 184);

        public Snapshot Snapshot { get; private set; } = Snapshot.Empty;
        private DayTotal _today;
        private DayTotal _session;
        private Font? _bigFont;
        private Font? _smallFont;
        private Font? _labelFont;

        private Font BigFont => _bigFont ??= new Font("Segoe UI Semibold", 17f, FontStyle.Bold);
        private Font SmallFont => _smallFont ??= new Font("Segoe UI", 8.5f);
        private Font LabelFont => _labelFont ??= new Font("Segoe UI", 9f, FontStyle.Bold);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _bigFont?.Dispose();
                _smallFont?.Dispose();
                _labelFont?.Dispose();
                _bigFont = _smallFont = _labelFont = null;
            }

            base.Dispose(disposing);
        }

        public SummaryBand()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = BandBack;
        }

        public void SetData(Snapshot snapshot, DayTotal today)
        {
            long rx = 0, tx = 0;
            foreach (var it in snapshot.Items)
            {
                rx += it.SessionRx;
                tx += it.SessionTx;
            }

            var session = new DayTotal(rx, tx);

            // repaint only when something drawn on the band actually changed
            bool changed = snapshot.DownRate != Snapshot.DownRate ||
                           snapshot.UpRate != Snapshot.UpRate ||
                           snapshot.Valid != Snapshot.Valid ||
                           today.Rx != _today.Rx || today.Tx != _today.Tx ||
                           session.Rx != _session.Rx || session.Tx != _session.Tx;

            Snapshot = snapshot;
            _today = today;
            _session = session;

            if (changed && !DebugFlags.NoPaintSummary) Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BandBack);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            int w = ClientSize.Width, h = ClientSize.Height;
            int pad = 16;

            var big = BigFont;
            var small = SmallFont;
            var label = LabelFont;
            {
                // ---- total rates ----
                int x = pad;
                var lblRect = new Rectangle(x, 8, 150, 16);
                TextRenderer.DrawText(g, Loc.T("main.total"), label, lblRect, Muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);

                var downRect = new Rectangle(x, 26, 220, 34);
                TextRenderer.DrawText(g, "↓ " + FormatUtil.Rate(Snapshot.DownRate), big, downRect, BandDown,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);

                var upRect = new Rectangle(x, 58, 220, 30);
                TextRenderer.DrawText(g, "↑ " + FormatUtil.Rate(Snapshot.UpRate), big, upRect, BandUp,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);

                if (!Snapshot.Valid)
                {
                    TextRenderer.DrawText(g, Loc.T("main.collecting"), small, new Rectangle(x + 230, 30, 260, 24), Muted,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
                }

                // ---- right hand totals ----
                int rx1 = w - pad - 210;
                int rx2 = w - pad - 96;
                if (rx1 > 260)
                {
                    DrawStat(g, Loc.T("main.today"), _today, rx1, small, label);
                    DrawStat(g, Loc.T("main.session"), _session, rx2, small, label);
                }
            }
        }

        private static void DrawStat(Graphics g, string caption, DayTotal total, int x, Font small, Font label)
        {
            TextRenderer.DrawText(g, caption, label, new Rectangle(x, 10, 110, 16), Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "↓ " + FormatUtil.Bytes(total.Rx), small, new Rectangle(x, 32, 110, 20), BandDown,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "↑ " + FormatUtil.Bytes(total.Tx), small, new Rectangle(x, 54, 110, 20), BandUp,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
        }
    }
}
