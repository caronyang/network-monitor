using NetworkMonitor.Core;

namespace NetworkMonitor.UI.Controls;

/// <summary>
/// Owner-drawn list of monitored processes - same visual language as the interface grid:
/// status dot, live down/up with relative bars, today and session totals. Each row has a
/// small × button (and a right-click menu) to remove the process from monitoring.
/// Rows only exist while the user has picked processes, so the control is never scrolled.
/// </summary>
public sealed class ProcessGridControl : Control
{
    private static readonly Color HeaderBack = Color.FromArgb(241, 244, 248);
    private static readonly Color HeaderText = Color.FromArgb(96, 110, 125);
    private static readonly Color RowAlt = Color.FromArgb(248, 250, 252);
    private static readonly Color RowLine = Color.FromArgb(230, 235, 241);
    private static readonly Color NameColor = Color.FromArgb(28, 40, 56);
    private static readonly Color SubColor = Color.FromArgb(130, 142, 156);
    private static readonly Color DownColor = Color.FromArgb(30, 136, 229);
    private static readonly Color UpColor = Color.FromArgb(251, 140, 0);
    private static readonly Color TrackColor = Color.FromArgb(226, 232, 240);
    private static readonly Color RunningColor = Color.FromArgb(46, 184, 92);
    private static readonly Color StoppedColor = Color.FromArgb(176, 185, 195);
    private static readonly Color CloseColor = Color.FromArgb(150, 160, 172);

    private static readonly SolidBrush AltBrush = new(RowAlt);
    private static readonly SolidBrush HeaderBrush = new(HeaderBack);
    private static readonly SolidBrush TrackBrush = new(TrackColor);
    private static readonly SolidBrush DownBrush = new(DownColor);
    private static readonly SolidBrush UpBrush = new(UpColor);
    private static readonly SolidBrush RunningBrush = new(RunningColor);
    private static readonly SolidBrush StoppedBrush = new(StoppedColor);
    private static readonly Pen RowPen = new(RowLine, 1f);
    private static readonly Pen ClosePen = new(Color.FromArgb(120, 130, 142), 1.4f);

    private IReadOnlyList<ProcInfo> _items = Array.Empty<ProcInfo>();
    private IReadOnlyDictionary<string, DayTotal> _today = new Dictionary<string, DayTotal>();
    private int _rowHeight = 46;
    private int _headerHeight = 30;
    private Font? _boldFont;
    private readonly ContextMenuStrip _menu = new();
    private string? _menuTarget;
    private readonly Rectangle[] _closeRects = new Rectangle[8];

    /// <summary>Raised with the canonical process name when the user removes one.</summary>
    public event Action<string>? RemoveRequested;

    public ProcessGridControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Color.White;
        Font = new Font("Segoe UI", 9f);

        _menu.ShowImageMargin = false;
        _menu.Font = new Font("Segoe UI", 9f);
        var item = new ToolStripMenuItem();
        item.Click += (_, _) =>
        {
            if (_menuTarget is not null) RemoveRequested?.Invoke(_menuTarget);
        };
        _menu.Items.Add(item);
        _menu.Opening += (_, _) =>
        {
            item.Text = Loc.T("proc.remove");
            item.Enabled = _menuTarget is not null;
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _boldFont?.Dispose();
            _menu.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>Preferred height for the current number of rows (used by MainForm).</summary>
    public int ContentHeight => _headerHeight + Math.Max(1, _items.Count) * _rowHeight + 4;

    /// <summary>Bold font shared by every row - created once instead of on every paint.</summary>
    private Font BoldFont => _boldFont ??= new Font(Font, FontStyle.Bold);

    public void SetData(IReadOnlyList<ProcInfo> items, IReadOnlyDictionary<string, DayTotal> today)
    {
        bool changed = HasChanged(items, today);
        _items = items;
        _today = today;
        if (changed) Invalidate();
    }

    private bool HasChanged(IReadOnlyList<ProcInfo> items, IReadOnlyDictionary<string, DayTotal> today)
    {
        if (!ReferenceEquals(today, _today)) return true;
        if (items.Count != _items.Count) return true;

        for (int i = 0; i < items.Count; i++)
        {
            var a = items[i];
            var b = _items[i];
            if (a.Name != b.Name || a.RxRate != b.RxRate || a.TxRate != b.TxRate ||
                a.SessionRx != b.SessionRx || a.SessionTx != b.SessionTx || a.Running != b.Running)
                return true;
        }

        return false;
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        _boldFont?.Dispose();
        _boldFont = null;
        _rowHeight = Math.Max(44, Font.Height * 2 + 12);
        _headerHeight = Math.Max(26, Font.Height + 14);
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        // fully painted in OnPaint
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);

        int width = ClientSize.Width;
        if (width < 50) return;

        var cols = Columns(width, StatusDotSize);
        DrawHeader(g, width, cols);

        if (_items.Count == 0)
        {
            var rect = new Rectangle(8, _headerHeight, width - 16, Math.Max(20, ClientSize.Height - _headerHeight));
            TextRenderer.DrawText(g, Loc.T("proc.empty"), Font, rect, SubColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }

        long maxRate = 1;
        foreach (var it in _items)
        {
            maxRate = Math.Max(maxRate, it.RxRate);
            maxRate = Math.Max(maxRate, it.TxRate);
        }

        int y = _headerHeight;
        for (int i = 0; i < _items.Count && i < _closeRects.Length; i++)
        {
            DrawRow(g, _items[i], i, y, width, maxRate, cols);
            y += _rowHeight;
        }
    }

    private int StatusDotSize => Math.Max(7, Font.Height - 6);

    private static int[] Columns(int width, int dotSize)
    {
        // name | down | up | today | session  -> 6 boundaries
        int x0 = 6 + dotSize + 6;
        int b1 = x0 + (int)(width * 0.25);
        int b2 = b1 + (int)(width * 0.19);
        int b3 = b2 + (int)(width * 0.18);
        int b4 = b3 + (int)(width * 0.18);
        int b5 = width - 14;
        return new[] { x0, b1, b2, b3, b4, b5 };
    }

    private void DrawHeader(Graphics g, int width, int[] cols)
    {
        g.FillRectangle(HeaderBrush, 0, 0, width, _headerHeight);

        var font = BoldFont;
        TextRenderer.DrawText(g, Loc.T("proc.name"), font, new Rectangle(cols[0], 0, cols[1] - cols[0], _headerHeight),
            HeaderText, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        DrawRight(g, Loc.T("main.down"), font, cols[1], cols[2], _headerHeight, HeaderText);
        DrawRight(g, Loc.T("main.up"), font, cols[2], cols[3], _headerHeight, HeaderText);
        DrawRight(g, Loc.T("main.today"), font, cols[3], cols[4], _headerHeight, HeaderText);
        DrawRight(g, Loc.T("main.session"), font, cols[4], cols[5], _headerHeight, HeaderText);
    }

    private static void DrawRight(Graphics g, string text, Font font, int left, int right, int height, Color color)
    {
        TextRenderer.DrawText(g, text, font, new Rectangle(left, 0, Math.Max(10, right - left - 10), height),
            color, TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }

    private void DrawRow(Graphics g, ProcInfo item, int index, int y, int width, long maxRate, int[] cols)
    {
        int h = _rowHeight;
        if (index % 2 == 1) g.FillRectangle(AltBrush, 0, y, width, h);
        g.DrawLine(RowPen, 0, y + h - 0.5f, width, y + h - 0.5f);

        int textTop = y + 5;
        int textH = Font.Height + 2;

        // ---- name + running state ----
        string display = item.Name + ".exe";
        int nameRight = cols[1] - 8 - 20; // leave room for the × button
        TextRenderer.DrawText(g, display, BoldFont, new Rectangle(cols[0], textTop, Math.Max(20, nameRight - cols[0]), textH),
            NameColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        string status = item.Running ? Loc.T("proc.running") : Loc.T("proc.notRunning");
        TextRenderer.DrawText(g, status, Font, new Rectangle(cols[0], textTop + textH, Math.Max(20, nameRight - cols[0]), textH),
            SubColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        int dotSize = StatusDotSize;
        g.FillEllipse(item.Running ? RunningBrush : StoppedBrush,
            6, y + (h - dotSize) / 2, dotSize, dotSize);

        // ---- × button (remove) ----
        int boxSize = Math.Max(18, Font.Height + 6);
        var closeRect = new Rectangle(cols[1] - 8 - boxSize, y + (h - boxSize) / 2, boxSize, boxSize);
        _closeRects[index] = Rectangle.Inflate(closeRect, 2, 2);
        var inner = new RectangleF(closeRect.X + 5, closeRect.Y + 5, closeRect.Width - 10, closeRect.Height - 10);
        using (var pen = new Pen(CloseColor, 1.4f))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.DrawLine(pen, inner.X, inner.Y, inner.Right, inner.Bottom);
            g.DrawLine(pen, inner.Right, inner.Y, inner.X, inner.Bottom);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;
        }

        // ---- rates + bars ----
        DrawRateCell(g, item.RxRate, cols[1], cols[2], textTop, textH, y, h, DownBrush, DownColor, maxRate);
        DrawRateCell(g, item.TxRate, cols[2], cols[3], textTop, textH, y, h, UpBrush, UpColor, maxRate);

        // ---- totals ----
        _today.TryGetValue(item.Key, out var today);
        DrawTotalsCell(g, today.Rx, today.Tx, cols[3], cols[4], textTop, textH);
        DrawTotalsCell(g, item.SessionRx, item.SessionTx, cols[4], cols[5], textTop, textH);
    }

    private void DrawRateCell(Graphics g, long rate, int left, int right, int textTop, int textH, int rowTop, int rowH,
        SolidBrush fillBrush, Color textColor, long maxRate)
    {
        int w = Math.Max(20, right - left - 16);
        TextRenderer.DrawText(g, FormatUtil.Rate(rate), Font, new Rectangle(left, textTop, w, textH),
            textColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        int barH = 5;
        int barY = textTop + textH + Math.Max(3, (rowH - (textTop - rowTop) - textH - barH) / 3);
        var track = new Rectangle(right - 16 - w, barY, w, barH);
        g.FillRectangle(TrackBrush, track);

        double frac = maxRate > 0 ? Math.Clamp((double)rate / maxRate, 0, 1) : 0;
        if (rate > 0 && frac < 0.02) frac = 0.02;
        int fillW = (int)Math.Round(w * frac);
        if (fillW > 0)
            g.FillRectangle(fillBrush, new Rectangle(track.X, track.Y, Math.Min(fillW, w), track.Height));
    }

    private void DrawTotalsCell(Graphics g, long rx, long tx, int left, int right, int textTop, int textH)
    {
        int w = Math.Max(30, right - left - 16);
        var top = new Rectangle(left, textTop, w, textH);
        var bottom = new Rectangle(left, textTop + textH + 2, w, textH);

        TextRenderer.DrawText(g, "↓ " + FormatUtil.Bytes(rx), Font, top, DownColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, "↑ " + FormatUtil.Bytes(tx), Font, bottom, UpColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button == MouseButtons.Left)
        {
            for (int i = 0; i < _items.Count && i < _closeRects.Length; i++)
            {
                if (_closeRects[i].Contains(e.Location))
                {
                    RemoveRequested?.Invoke(_items[i].Name);
                    return;
                }
            }
        }
        else if (e.Button == MouseButtons.Right)
        {
            int idx = RowIndexAt(e.Y);
            _menuTarget = idx >= 0 ? _items[idx].Name : null;
            if (_menuTarget is not null) _menu.Show(this, e.Location);
        }
    }

    private int RowIndexAt(int y)
    {
        if (y < _headerHeight) return -1;
        int idx = (y - _headerHeight) / _rowHeight;
        return idx >= 0 && idx < _items.Count ? idx : -1;
    }
}
