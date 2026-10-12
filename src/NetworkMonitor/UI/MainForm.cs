using NetworkMonitor.Core;
using NetworkMonitor.UI.Controls;

namespace NetworkMonitor.UI;

/// <summary>
/// Main dashboard: summary band + process monitoring row + owner-drawn process grid +
/// owner-drawn interface grid. All data is pushed into the form by
/// <see cref="TrayApplicationContext"/>'s 1 Hz timer.
/// </summary>
public sealed class MainForm : Form, ILocalizable
{
    private readonly AppSettings _settings;
    private readonly TrafficDatabase _db;
    private readonly NetworkSampler _sampler;

    private readonly Panel _toolbar = new();
    private readonly Label _title = new();
    private readonly Button _btnStats = new();
    private readonly Button _btnSettings = new();

    private readonly SummaryBand _summary = new();
    private readonly Panel _procOps = new();
    private readonly Label _procTitle = new();
    private readonly Button _btnAddProc = new();
    private readonly Label _hintAdmin = new();
    private readonly ProcessGridControl _procGrid = new();
    private readonly Panel _procHost = new();
    private readonly InterfaceGridControl _grid = new();
    private readonly Panel _gridHost = new();
    private readonly ToolTip _tooltips = new();
    private readonly Panel _swBar = new();
    private readonly Label _swTitle = new();
    private readonly Button _swStart = new();
    private readonly Button _swStop = new();
    private readonly Label _swStatus = new();

    private int _lastProcCount = -1;
    private IReadOnlyDictionary<string, DayTotal> _today = new Dictionary<string, DayTotal>();
    private bool _todayQueryRunning;
    private DateTime _lastTodayQuery = DateTime.MinValue;

    public event Action? RequestStats;
    public event Action? RequestSettings;

    /// <summary>Raised when the user asks to relaunch the app elevated (process monitoring needs admin).</summary>
    public event Action? RequestRestartElevated;

    private readonly int _initialW;
    private readonly int _initialH;

    public MainForm(AppSettings settings, TrafficDatabase db, NetworkSampler sampler)
    {
        _settings = settings;
        _db = db;
        _sampler = sampler;

        // Capture the intended size before any layout pass can overwrite it through OnResize
        // (the first layout/auto-scale pass clamps the form to MinimumSize).
        _initialW = Math.Max(720, _settings.MainWidth ?? 720);
        _initialH = Math.Max(526, _settings.MainHeight ?? 526);

        SuspendLayout();

        Name = "MainForm";
        Text = Loc.T("main.title");
        // 720x526 = the v1.1.1 minimum plus one list row (~46 px) so the interface list
        // shows noticeably more entries by default.
        MinimumSize = new Size(720, 526);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.White;
        ShowInTaskbar = true;

        Size = new Size(_initialW, _initialH);

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

        // ---- process monitoring ops row ----
        _procOps.Dock = DockStyle.Top;
        _procOps.Height = 40;
        _procOps.BackColor = Color.FromArgb(247, 249, 252);
        _procOps.Padding = new Padding(12, 0, 12, 0);

        var opsFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = false,
        };

        _procTitle.AutoSize = true;
        _procTitle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        _procTitle.ForeColor = Color.FromArgb(45, 62, 80);
        _procTitle.Margin = new Padding(0, 11, 8, 0);

        ConfigureButton(_btnAddProc, (_, _) => ShowAddProcess());
        _btnAddProc.Margin = new Padding(0, 5, 0, 0);
        _tooltips.SetToolTip(_btnAddProc, Loc.T("proc.tipRemove"));

        _hintAdmin.AutoSize = true;
        _hintAdmin.Font = new Font("Segoe UI", 9f);
        _hintAdmin.ForeColor = Color.FromArgb(196, 43, 28);
        _hintAdmin.Margin = new Padding(14, 11, 6, 0);
        _hintAdmin.Cursor = Cursors.Hand;
        _hintAdmin.Click += (_, _) => AskElevateIfNeeded();
        _hintAdmin.Visible = false;

        opsFlow.Controls.Add(_procTitle);
        opsFlow.Controls.Add(_btnAddProc);
        opsFlow.Controls.Add(_hintAdmin);
        _procOps.Controls.Add(opsFlow);

        // ---- process grid (only visible while something is monitored) ----
        // The grid itself is never scrolled: it spans the full content height inside an
        // AutoScroll host, so scrolling moves a child control instead of blitting an
        // owner-drawn surface (which would smear the rows).
        _procHost.Dock = DockStyle.Top;
        _procHost.AutoScroll = true;
        _procHost.BackColor = Color.White;
        _procHost.Visible = false;
        _procHost.Height = 0;

        _procGrid.Dock = DockStyle.Top;
        _procGrid.RemoveRequested += RemoveProcess;
        _procHost.Controls.Add(_procGrid);

        // ---- traffic stopwatch bottom bar ----
        _swBar.Dock = DockStyle.Bottom;
        _swBar.Height = 46;
        _swBar.BackColor = Color.FromArgb(247, 249, 252);
        _swBar.Padding = new Padding(12, 0, 12, 0);

        var swFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = false,
        };

        _swTitle.AutoSize = true;
        _swTitle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        _swTitle.ForeColor = Color.FromArgb(45, 62, 80);
        _swTitle.Margin = new Padding(0, 11, 10, 0);

        ConfigureButton(_swStart, (_, _) => { _sampler.StartMeasurement(); UpdateMeasurementUi(); });
        _swStart.Margin = new Padding(0, 5, 0, 0);
        _tooltips.SetToolTip(_swStart, Loc.T("sw.tip"));
        ConfigureButton(_swStop, (_, _) => { _sampler.StopMeasurement(); UpdateMeasurementUi(); });
        _swStop.Margin = new Padding(0, 5, 0, 0);
        _tooltips.SetToolTip(_swStop, Loc.T("sw.tip"));

        _swStatus.AutoSize = true;
        _swStatus.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        _swStatus.ForeColor = Color.FromArgb(184, 92, 0);
        _swStatus.Margin = new Padding(14, 11, 0, 0);
        _swStatus.Visible = false;

        swFlow.Controls.Add(_swTitle);
        swFlow.Controls.Add(_swStart);
        swFlow.Controls.Add(_swStop);
        swFlow.Controls.Add(_swStatus);
        _swBar.Controls.Add(swFlow);

        // ---- grid ----
        _gridHost.Dock = DockStyle.Fill;
        _gridHost.AutoScroll = true;
        _gridHost.BackColor = Color.White;
        _grid.Dock = DockStyle.Top;
        _gridHost.Controls.Add(_grid);

        Controls.Add(_gridHost);
        Controls.Add(_swBar);
        Controls.Add(_procHost);
        Controls.Add(_procOps);
        Controls.Add(_summary);
        Controls.Add(_toolbar);

        ApplyLocalization();
        UpdateMeasurementUi();
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
        _swTitle.Text = Loc.T("sw.title");
        _swStart.Text = Loc.T("sw.start");
        _swStop.Text = Loc.T("sw.stop");
        _btnAddProc.Text = Loc.T("proc.add");
        _hintAdmin.Text = Loc.T("proc.adminHintSmall");
        _tooltips.SetToolTip(_swStart, Loc.T("sw.tip"));
        _tooltips.SetToolTip(_swStop, Loc.T("sw.tip"));
        _tooltips.SetToolTip(_btnAddProc, Loc.T("proc.tipRemove"));
        UpdateProcOpsUi(ProcessMonitorState.Stopped, force: true);
        UpdateMeasurementUi();
        _summary.Invalidate();
        _grid.Invalidate();
        _procGrid.Invalidate();
    }

    /// <summary>Called by the tray context at 1 Hz.</summary>
    public void PushSnapshot(Snapshot snapshot)
    {
        if (!Visible || IsDisposed) return;

        _summary.SetData(snapshot, SumToday());
        _grid.SetData(snapshot.Items, _today);
        _procGrid.SetData(snapshot.Processes, _today);

        var state = _sampler.Processes?.State ?? ProcessMonitorState.Stopped;
        UpdateProcOpsUi(state);
        UpdateMeasurementUi();
        RefreshTodayIfNeeded(snapshot);
    }

    private void UpdateProcOpsUi(ProcessMonitorState state, bool force = false)
    {
        int n = _settings.ProcessMonitors.Count;
        _procTitle.Text = Loc.T("proc.count", n, AppSettings.MaxProcessMonitors);

        bool gridVisible = n > 0;
        if (gridVisible != _procHost.Visible)
        {
            _procHost.Visible = gridVisible;
            if (!gridVisible) _procHost.Height = 0;
        }

        if (gridVisible)
        {
            int rows = Math.Min(n, ProcessGridControl.VisibleRowCap);
            int viewport = _procGrid.HeightForRows(rows);
            if (_procHost.Height != viewport) _procHost.Height = viewport;
            else if (force) _procGrid.Invalidate();
        }
        // admin hint shows when names are monitored but the ETW session cannot run;
        // clicking it opens the elevation question dialog
        bool showAdminHint = n > 0 && state != ProcessMonitorState.Running;
        if (showAdminHint != _hintAdmin.Visible || force)
        {
            _hintAdmin.Visible = showAdminHint;
            _hintAdmin.Text = state switch
            {
                ProcessMonitorState.RequiresAdmin => Loc.T("proc.adminHintSmall"),
                ProcessMonitorState.Failed => Loc.T("proc.failed"),
                _ => Loc.T("proc.adminHintSmall"),
            };
            _tooltips.SetToolTip(_hintAdmin,
                state == ProcessMonitorState.Failed
                    ? (_sampler.Processes?.LastError ?? Loc.T("proc.failed"))
                    : Loc.T("proc.adminNeeded"));
        }

        _lastProcCount = n;
    }

    private void ShowAddProcess()
    {
        using var dlg = new ProcessPickerDialog(_settings.ProcessMonitors);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        _settings.ProcessMonitors.AddRange(dlg.SelectedNames);
        _settings.Normalize(); // dedupe + cap at 5
        _settings.Save();
        UpdateProcOpsUi(_sampler.Processes?.State ?? ProcessMonitorState.Stopped, force: true);
        AskElevateIfNeeded();
    }

    /// <summary>Asks (modal dialog) whether to relaunch elevated for process monitoring.</summary>
    private void AskElevateIfNeeded()
    {
        var state = _sampler.Processes?.State ?? ProcessMonitorState.Stopped;
        int count = _settings.ProcessMonitors.Count;
        if (count == 0 || state == ProcessMonitorState.Running) return;

        using var prompt = new AdminPromptDialog();
        if (prompt.ShowDialog(this) == DialogResult.Yes)
            RequestRestartElevated?.Invoke();
    }

    private void RemoveProcess(string name)
    {
        if (_settings.ProcessMonitors.RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) == 0)
            return;
        _settings.Save();
        UpdateProcOpsUi(_sampler.Processes?.State ?? ProcessMonitorState.Stopped, force: true);
    }

    private void UpdateMeasurementUi()
    {
        bool measuring = _sampler.Measuring;
        _swStart.Enabled = !measuring;
        _swStop.Enabled = measuring;

        if (measuring)
        {
            var span = DateTime.Now - _sampler.MeasurementStart;
            _swStatus.Text = Loc.T("sw.running",
                span.ToString(@"hh\:mm\:ss"),
                FormatUtil.Bytes(_sampler.MeasurementRx),
                FormatUtil.Bytes(_sampler.MeasurementTx));
            _swStatus.Visible = true;
        }
        else if (_swStatus.Text.Length > 0)
        {
            _swStatus.Text = "";
            _swStatus.Visible = false;
        }
    }

    private DayTotal SumToday()
    {
        long rx = 0, tx = 0;
        foreach (var kv in _today)
        {
            if (TrafficKeys.IsProcess(kv.Key)) continue; // processes are not part of "all interfaces"
            rx += kv.Value.Rx;
            tx += kv.Value.Tx;
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
                        _procGrid.SetData(snapshot.Processes, _today);
                    }
                });
            }
            catch
            {
                _todayQueryRunning = false;
            }
        });
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // Applied here (not in the constructor) because the first layout pass would clamp
        // a constructor-set Size while the handle does not exist yet.
        Size = new Size(_initialW, _initialH);
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
