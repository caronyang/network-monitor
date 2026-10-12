using NetworkMonitor.Core;
using NetworkMonitor.UI.Controls;
using Label = System.Windows.Forms.Label;

namespace NetworkMonitor.UI;

/// <summary>
/// Statistics window: line / bar charts of download and upload traffic with
/// Day (hourly), Week (daily) and Month (daily) aggregation.
/// </summary>
public sealed class StatsForm : Form, ILocalizable
{
    /// <summary>CJK-capable font for SkiaSharp text (no font fallback in SkiaSharp).</summary>
    private const string CjkFontName = "Microsoft JhengHei UI";

    private readonly TrafficDatabase _db;
    private readonly NetworkSampler _sampler;

    private readonly Panel _toolbar = new();
    private readonly FlowLayoutPanel _row1 = new();
    private readonly Panel _row2 = new();
    private readonly Label _title = new();
    private readonly ComboBox _cbChart = new();
    private readonly ComboBox _cbPeriod = new();
    private readonly ComboBox _cbIface = new();
    private readonly Button _btnPrev = new();
    private readonly Button _btnNext = new();
    private readonly Button _btnRefresh = new();
    private readonly Button _btnExport = new();
    private readonly Label _range = new();
    private readonly Label _totals = new();
    private readonly ScottPlot.WinForms.FormsPlot _plot = new();
    private readonly Panel _recordsPanel = new();
    private readonly Label _recordsHeader = new();
    private readonly ListView _records = new();
    private List<string> _processKeys = new();

    private string _chart = "line";
    private string _period = "day";
    private int _offset;                 // 0 = current period, -1 = previous one, ...
    private bool _loading;
    private bool _pendingLoad;
    private bool _filling;                 // true while combo boxes are repopulated
    private DateTime _lastLoadUtc = DateTime.MinValue;

    public StatsForm(TrafficDatabase db, NetworkSampler sampler)
    {
        _db = db;
        _sampler = sampler;

        SuspendLayout();

        Name = "StatsForm";
        Text = Loc.T("stats.title");
        // toolbar row needs ~954px (title + 3 combos + four 84px buttons incl. margins);
        // anything narrower clips "Export CSV" at the right edge.
        MinimumSize = new Size(990, 560);
        Size = new Size(1000, 640);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.White;
        KeyPreview = true;

        // ---------- toolbar ----------
        _toolbar.Dock = DockStyle.Top;
        _toolbar.Height = 82;
        _toolbar.BackColor = Color.FromArgb(247, 249, 252);
        _toolbar.Padding = new Padding(10, 4, 10, 2);

        _row1.Dock = DockStyle.Top;
        _row1.Height = 44;
        _row1.WrapContents = false;
        _row1.AutoScroll = false;
        _row1.Padding = new Padding(2, 6, 0, 0);

        _title.Text = Loc.T("stats.title");
        _title.Font = new Font("Segoe UI Semibold", 12.5f, FontStyle.Bold);
        _title.ForeColor = Color.FromArgb(28, 40, 56);
        _title.AutoSize = true;
        _title.Margin = new Padding(0, 4, 18, 0);
        _title.TextAlign = ContentAlignment.MiddleLeft;

        StyleCombo(_cbChart, 140);
        StyleCombo(_cbPeriod, 116);
        StyleCombo(_cbIface, 210);
        _cbChart.SelectedIndexChanged += (_, _) => { if (_filling) return; OnSelectionChanged(); };
        _cbPeriod.SelectedIndexChanged += (_, _) => { if (_filling) return; _period = SelectedKey(_cbPeriod) ?? "day"; _offset = 0; OnSelectionChanged(); };
        _cbIface.SelectedIndexChanged += (_, _) => { if (_filling) return; OnSelectionChanged(); };

        StyleButton(_btnPrev, "<", (_, _) => { _offset--; LoadData(force: true); });
        StyleButton(_btnNext, ">", (_, _) => { _offset++; LoadData(force: true); });
        StyleButton(_btnRefresh, Loc.T("stats.refresh"), (_, _) => LoadData(force: true));
        StyleButton(_btnExport, Loc.T("stats.export"), (_, _) => ExportCsv());

        // Diagnostic hook (like NM_NOPAINT): e.g. NM_STATS="chart=bar period=week offset=-1"
        var diagStats = Environment.GetEnvironmentVariable("NM_STATS");
        if (!string.IsNullOrEmpty(diagStats))
        {
            foreach (var part in diagStats.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split('=');
                if (kv.Length != 2) continue;
                switch (kv[0])
                {
                    case "chart": _chart = kv[1]; break;
                    case "period": _period = kv[1]; break;
                    case "offset": if (int.TryParse(kv[1], out var off)) _offset = off; break;
                }
            }
        }

        _row1.Controls.Add(_title);
        _row1.Controls.Add(_cbChart);
        _row1.Controls.Add(_cbPeriod);
        _row1.Controls.Add(_cbIface);
        _row1.Controls.Add(_btnPrev);
        _row1.Controls.Add(_btnNext);
        _row1.Controls.Add(_btnRefresh);
        _row1.Controls.Add(_btnExport);

        _row2.Dock = DockStyle.Fill;
        _row2.Padding = new Padding(4, 0, 4, 0);

        _range.AutoSize = false;
        _range.Dock = DockStyle.Left;
        _range.Width = 460;
        _range.ForeColor = Color.FromArgb(96, 110, 125);
        _range.Font = new Font("Segoe UI", 9f);
        _range.TextAlign = ContentAlignment.MiddleLeft;

        _totals.AutoSize = true;
        _totals.Dock = DockStyle.Right;
        _totals.ForeColor = Color.FromArgb(96, 110, 125);
        _totals.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        _totals.TextAlign = ContentAlignment.MiddleRight;
        _totals.Padding = new Padding(0, 0, 6, 0);

        _row2.Controls.Add(_totals);
        _row2.Controls.Add(_range);

        _toolbar.Controls.Add(_row2);
        _toolbar.Controls.Add(_row1);

        _plot.Dock = DockStyle.Fill;
        _plot.BackColor = Color.White;

        // ---------- traffic-stopwatch records ----------
        _recordsPanel.Dock = DockStyle.Bottom;
        _recordsPanel.Height = 190;
        _recordsPanel.Padding = new Padding(10, 6, 10, 8);
        _recordsPanel.BackColor = Color.White;

        _recordsHeader.Text = Loc.T("stats.records");
        _recordsHeader.Dock = DockStyle.Top;
        _recordsHeader.AutoSize = false;
        _recordsHeader.Height = 26;
        _recordsHeader.Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold);
        _recordsHeader.ForeColor = Color.FromArgb(28, 40, 56);

        _records.Dock = DockStyle.Fill;
        _records.View = View.Details;
        _records.FullRowSelect = true;
        _records.GridLines = true;
        _records.MultiSelect = false;
        _records.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _records.Font = new Font("Segoe UI", 9f);
        _records.BorderStyle = BorderStyle.None;
        _records.Columns.Add(Loc.T("stats.recStart"), 155);
        _records.Columns.Add(Loc.T("stats.recEnd"), 155);
        _records.Columns.Add(Loc.T("stats.recDuration"), 90, HorizontalAlignment.Right);
        _records.Columns.Add(Loc.T("main.down"), 120, HorizontalAlignment.Right);
        _records.Columns.Add(Loc.T("main.up"), 120, HorizontalAlignment.Right);

        _recordsPanel.Controls.Add(_records);
        _recordsPanel.Controls.Add(_recordsHeader);

        Controls.Add(_plot);
        Controls.Add(_recordsPanel);
        Controls.Add(_toolbar);

        PopulateChoices();
        ApplyLocalization();

        Load += (_, _) =>
        {
            LoadData(force: true);
            RefreshRecords();
            BeginInvoke(new Action(ForceToolbarPaint));
        };
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Left) { _offset--; LoadData(force: true); }
            else if (e.KeyCode == Keys.Right) { _offset++; LoadData(force: true); }
            else if (e.KeyCode == Keys.F5) LoadData(force: true);
        };

        ResumeLayout(false);
    }

    private static void StyleCombo(ComboBox cb, int width)
    {
        cb.FlatStyle = FlatStyle.Flat;
        cb.Font = new Font("Segoe UI", 9f);
        cb.Width = width;
        cb.Height = 30;
        cb.DropDownStyle = ComboBoxStyle.DropDownList;
        cb.Margin = new Padding(4, 3, 0, 0);
        cb.Padding = new Padding(4, 5, 4, 4);
    }

    private static void StyleButton(Button b, string text, EventHandler onClick)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderColor = Color.FromArgb(214, 222, 232);
        b.BackColor = Color.White;
        b.ForeColor = Color.FromArgb(45, 62, 80);
        b.Font = new Font("Segoe UI", 9f);
        // 84px: widest label ("Export CSV" = 65px at 9pt) must fit on ONE line inside
        // the ~74px text area, otherwise WordBreak measurement returns a 2-line height,
        // TextBounds becomes full-height and the top-aligned text sits ~3px too high.
        b.Width = 84;
        b.Height = 32;
        b.Margin = new Padding(6, 2, 0, 0);
        b.Cursor = Cursors.Hand;
        b.Click += onClick;
        b.Text = text;
    }

    private void OnSelectionChanged()
    {
        _chart = SelectedKey(_cbChart) ?? "line";
        LoadData(force: true);
    }

    private static string? SelectedKey(ComboBox cb) => (cb.SelectedItem as Choice)?.Key;

    private void Fill(ComboBox cb, IEnumerable<Choice> items, string? preferred)
    {
        _filling = true;
        try
        {
            cb.Items.Clear();
            Choice? match = null;
            foreach (var c in items)
            {
                cb.Items.Add(c);
                if (c.Key == preferred) match = c;
            }

            cb.SelectedItem = match ?? (cb.Items.Count > 0 ? cb.Items[0] : null);
        }
        finally
        {
            _filling = false;
        }
    }

    private void PopulateChoices()
    {
        Fill(_cbChart, new[]
        {
            new Choice("line", Loc.T("stats.line")),
            new Choice("bar", Loc.T("stats.bar")),
        }, _chart);

        Fill(_cbPeriod, new[]
        {
            new Choice("day", Loc.T("stats.day")),
            new Choice("week", Loc.T("stats.week")),
            new Choice("month", Loc.T("stats.month")),
        }, _period);

        var ifaceItems = new List<Choice> { new(null, Loc.T("stats.all")) };
        foreach (var it in _sampler.Latest.Items)
            ifaceItems.Add(new Choice(it.Id, it.Name));
        foreach (var key in _processKeys)
            ifaceItems.Add(new Choice(key, Loc.T("stats.processLabel", TrafficKeys.ProcessDisplayName(key))));

        string? current = (_cbIface.SelectedItem as Choice)?.Key;
        Fill(_cbIface, ifaceItems, current);
    }

    public void ApplyLocalization()
    {
        Text = Loc.T("stats.title");
        _title.Text = Loc.T("stats.title");
        _btnRefresh.Text = Loc.T("stats.refresh");
        _btnExport.Text = Loc.T("stats.export");
        _recordsHeader.Text = Loc.T("stats.records");
        if (_records.Columns.Count >= 5)
        {
            _records.Columns[0].Text = Loc.T("stats.recStart");
            _records.Columns[1].Text = Loc.T("stats.recEnd");
            _records.Columns[2].Text = Loc.T("stats.recDuration");
            _records.Columns[3].Text = Loc.T("main.down");
            _records.Columns[4].Text = Loc.T("main.up");
        }

        PopulateChoices();
        RefreshRecords();
        if (Visible && !IsDisposed) LoadData(force: true);
    }

    private void BuildPeriodRange(out int fromDay, out int toDay, out int buckets, out bool hourly)
    {
        int today = DateUtil.DayKey(DateTime.Today);
        switch (_period)
        {
            case "week":
                hourly = false;
                buckets = 7;
                toDay = DateUtil.DayKeyOf(today, _offset * 7);
                fromDay = DateUtil.DayKeyOf(toDay, -6);
                break;
            case "month":
                hourly = false;
                buckets = 30;
                toDay = DateUtil.DayKeyOf(today, _offset * 30);
                fromDay = DateUtil.DayKeyOf(toDay, -29);
                break;
            default:
                hourly = true;
                buckets = 24;
                fromDay = toDay = DateUtil.DayKeyOf(today, _offset);
                break;
        }
    }

    private async void LoadData(bool force = false)
    {
        if (_loading)
        {
            _pendingLoad = true;
            return;
        }

        if (!force && (DateTime.UtcNow - _lastLoadUtc).TotalSeconds < 30) return;

        _loading = true;
        _lastLoadUtc = DateTime.UtcNow;
        PopulateChoices();

        BuildPeriodRange(out int fromDay, out int toDay, out int buckets, out bool hourly);
        string? iface = (_cbIface.SelectedItem as Choice)?.Key;

        try
        {
            var (data, procKeys) = await Task.Run(() =>
            {
                var d = hourly ? _db.QueryDay(fromDay, iface) : _db.QueryDays(fromDay, toDay, iface);
                return (d, _db.ProcessKeys());
            });

            _processKeys = procKeys;
            PopulateChoices();
            Render(data, fromDay, toDay, buckets, hourly);
        }
        catch
        {
            // never let a chart error take the window down
        }
        finally
        {
            _loading = false;
            ForceToolbarPaint();
            if (_pendingLoad)
            {
                _pendingLoad = false;
                LoadData(force: true);
            }
        }
    }

    /// <summary>
    /// Synchronously repaints the toolbar and summary rows. Some machines drop the first
    /// WM_PAINT for freshly filled controls (buttons/combo boxes keep a blank face), so
    /// every state change that alters their text forces one immediate paint pass.
    /// </summary>
    private void ForceToolbarPaint()
    {
        foreach (Control c in _row1.Controls) { c.Invalidate(); c.Update(); }
        _range.Invalidate(); _range.Update();
        _totals.Invalidate(); _totals.Update();
        _toolbar.Invalidate();
        _toolbar.Update();
    }

    private void Render(List<Bucket> data, int fromDay, int toDay, int buckets, bool hourly)
    {
        long[] rx = new long[buckets];
        long[] tx = new long[buckets];
        foreach (var b in data)
        {
            int idx = hourly ? b.Key : DayIndex(fromDay, b.Key);
            if (idx >= 0 && idx < buckets)
            {
                rx[idx] = b.Rx;
                tx[idx] = b.Tx;
            }
        }

        long max = Math.Max(1, Math.Max(rx.Max(), tx.Max()));
        double divisor = UnitDivisor(max);
        string unit = UnitName(divisor);

        double[] xs = Enumerable.Range(0, buckets).Select(i => (double)i).ToArray();
        double[] rxD = rx.Select(v => v / divisor).ToArray();
        double[] txD = tx.Select(v => v / divisor).ToArray();

        string[] labels = hourly
            ? Enumerable.Range(0, 24).Select(h => h.ToString("00")).ToArray()
            : Enumerable.Range(0, buckets).Select(i => DayLabel(DateUtil.DayKeyOf(fromDay, i))).ToArray();

        var plot = _plot.Plot;
        plot.Clear();

        // ScottPlot ships with a benchmark plottable. Assigning null to Plot.Benchmark
        // makes the next render throw, so hide it instead of removing it.
        if (plot.Benchmark is { } bench) bench.IsVisible = false;

        var down = ScottPlot.Colors.DodgerBlue;
        var up = ScottPlot.Colors.Orange;

        if (_chart == "bar")
        {
            double size = buckets <= 7 ? 0.36 : buckets <= 10 ? 0.4 : 0.34;
            var barsDown = new ScottPlot.Bar[buckets];
            var barsUp = new ScottPlot.Bar[buckets];
            for (int i = 0; i < buckets; i++)
            {
                barsDown[i] = new ScottPlot.Bar { Position = i - size / 2 - 0.02, Value = rxD[i], Size = size, FillColor = down };
                barsUp[i] = new ScottPlot.Bar { Position = i + size / 2 + 0.02, Value = txD[i], Size = size, FillColor = up };
            }

            var bp1 = plot.Add.Bars(barsDown);
            bp1.LegendText = Loc.T("stats.rx");
            var bp2 = plot.Add.Bars(barsUp);
            bp2.LegendText = Loc.T("stats.tx");
        }
        else
        {
            var s1 = plot.Add.Scatter(xs, rxD, down);
            s1.LineWidth = 2.2f;
            s1.MarkerSize = 0;
            s1.LegendText = Loc.T("stats.rx");

            var s2 = plot.Add.Scatter(xs, txD, up);
            s2.LineWidth = 2.2f;
            s2.MarkerSize = 0;
            s2.LegendText = Loc.T("stats.tx");
        }

        plot.Axes.Bottom.SetTicks(xs, labels);
        plot.Axes.Left.Label.Text = $"{Loc.T("stats.axis")} ({unit})";
        plot.Axes.Left.TickLabelStyle.FontSize = 11;
        plot.Axes.Bottom.TickLabelStyle.FontSize = 11;
        plot.Axes.Title.Label.Text = RangeText(fromDay, toDay, hourly);
        plot.Axes.Title.Label.FontSize = 13;
        plot.Axes.DefaultGrid.IsVisible = true;
        plot.Axes.AutoScale();

        // no negative axis for byte counts
        var limits = plot.Axes.GetLimits();
        plot.Axes.SetLimitsY(0, Math.Max(1, limits.Top));

        var legend = plot.ShowLegend(ScottPlot.Alignment.UpperRight);
        // SkiaSharp performs no font fallback and Segoe UI has no CJK glyphs,
        // so Chinese text would render as tofu boxes without an explicit font.
        legend.FontName = CjkFontName;
        plot.Axes.Left.Label.FontName = CjkFontName;
        _plot.Refresh();

        long totalRx = rx.Sum(), totalTx = tx.Sum();
        string hint = Loc.T(hourly ? "stats.dayHint" : _period == "week" ? "stats.weekHint" : "stats.monthHint");
        _range.Text = $"{Loc.T("stats.range")}: {RangeText(fromDay, toDay, hourly)} · {hint}";
        _totals.Text =
            $"{Loc.T("stats.totalRx")}: {FormatUtil.Bytes(totalRx)}     {Loc.T("stats.totalTx")}: {FormatUtil.Bytes(totalTx)}";

        if (totalRx == 0 && totalTx == 0)
            _range.Text += " · " + Loc.T("stats.noData");
    }

    private static double UnitDivisor(long maxBytes)
    {
        if (maxBytes >= 1_000_000_000) return 1_000_000_000d;
        if (maxBytes >= 1_000_000) return 1_000_000d;
        if (maxBytes >= 1_000) return 1_000d;
        return 1d;
    }

    private static string UnitName(double divisor) => divisor switch
    {
        >= 1_000_000_000d => "GB",
        >= 1_000_000d => "MB",
        >= 1_000d => "KB",
        _ => "B",
    };

    private static int DayIndex(int fromDay, int key)
    {
        if (key < fromDay) return -1;
        int idx = 0;
        int cur = fromDay;
        while (cur < key && idx < 400)
        {
            cur = DateUtil.DayKeyOf(cur, 1);
            idx++;
        }

        return cur == key ? idx : -1;
    }

    private static string DayLabel(int dayKey) => DateUtil.FromDayKey(dayKey).ToString("M/d");

    private string RangeText(int fromDay, int toDay, bool hourly)
    {
        var a = DateUtil.FromDayKey(fromDay);
        var b = DateUtil.FromDayKey(toDay);
        const string fmt = "yyyy-MM-dd";
        if (hourly) return a.ToString(fmt);
        return fromDay == toDay ? a.ToString(fmt) : $"{a.ToString(fmt)} ~ {b.ToString(fmt)}";
    }

    private void ExportCsv()
    {
        BuildPeriodRange(out int fromDay, out int toDay, out _, out bool hourly);
        string? iface = (_cbIface.SelectedItem as Choice)?.Key;
        List<Bucket> data = hourly ? _db.QueryDay(fromDay, iface) : _db.QueryDays(fromDay, toDay, iface);

        try
        {
            Directory.CreateDirectory(AppSettings.DataFolder);
            string safe = (iface ?? "all").Replace(" ", "_").Replace(':', '_');
            string file = Path.Combine(AppSettings.DataFolder, $"traffic_{fromDay}_{toDay}_{safe}.csv");
            using (var writer = new StreamWriter(file, false, new System.Text.UTF8Encoding(true)))
            {
                writer.WriteLine("key,download_bytes,upload_bytes");
                foreach (var b in data) writer.WriteLine($"{b.Key},{b.Rx},{b.Tx}");
            }

            MessageBox.Show(this, Loc.T("stats.saved", file), Loc.T("stats.title"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Loc.T("common.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Keeps the visible chart fresh without user interaction.</summary>
    public void AutoRefresh()
    {
        if (!Visible || IsDisposed || _loading) return;
        if ((DateTime.UtcNow - _lastLoadUtc).TotalSeconds < 60) return;
        RefreshRecords();
        LoadData(force: true);
    }

    /// <summary>Reloads the stopwatch-record list off the UI thread.</summary>
    public void RefreshRecords()
    {
        if (IsDisposed) return;
        Task.Run(() =>
        {
            var rows = _db.QueryMeasurements(300);
            try
            {
                BeginInvoke(new Action(() => FillRecords(rows)));
            }
            catch
            {
                // window handle not created yet or already disposed - a later refresh will catch up
            }
        });
    }

    private void FillRecords(List<Measurement> rows)
    {
        if (IsDisposed) return;
        _records.BeginUpdate();
        _records.Items.Clear();
        foreach (var m in rows)
        {
            var start = DateTimeOffset.FromUnixTimeSeconds(m.StartUnix).LocalDateTime;
            var end = DateTimeOffset.FromUnixTimeSeconds(m.EndUnix).LocalDateTime;
            var item = new ListViewItem(start.ToString("yyyy-MM-dd HH:mm:ss"));
            item.SubItems.Add(end.ToString("yyyy-MM-dd HH:mm:ss"));
            item.SubItems.Add((end - start).ToString(@"hh\:mm\:ss"));
            item.SubItems.Add(FormatUtil.Bytes(m.Rx));
            item.SubItems.Add(FormatUtil.Bytes(m.Tx));
            _records.Items.Add(item);
        }

        if (rows.Count == 0)
        {
            var empty = new ListViewItem(Loc.T("stats.noRecords")) { ForeColor = Color.FromArgb(150, 160, 172) };
            empty.SubItems.Add("");
            empty.SubItems.Add("");
            empty.SubItems.Add("");
            empty.SubItems.Add("");
            _records.Items.Add(empty);
        }

        _records.EndUpdate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Hide();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private sealed record Choice(string? Key, string Text)
    {
        public override string ToString() => Text;
    }
}

