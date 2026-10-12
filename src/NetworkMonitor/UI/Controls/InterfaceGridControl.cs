using NetworkMonitor.Core;

namespace NetworkMonitor.UI.Controls;

/// <summary>
/// Owner-drawn, double-buffered list of network interfaces. Everything is painted in a
/// single pass at 1 Hz, so it costs almost no CPU and allocates nothing.
/// </summary>
public sealed class InterfaceGridControl : ScrollableControl
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
    private static readonly Color ConnectedColor = Color.FromArgb(46, 184, 92);
    private static readonly Color DisconnectedColor = Color.FromArgb(176, 185, 195);

    // Shared GDI objects: creating and disposing brushes/pens on every 1 Hz repaint is
    // measurable overhead, so they live for the whole process instead.
    private static readonly SolidBrush AltBrush = new(RowAlt);
    private static readonly SolidBrush HeaderBrush = new(HeaderBack);
    private static readonly SolidBrush TrackBrush = new(TrackColor);
    private static readonly SolidBrush DownBrush = new(DownColor);
    private static readonly SolidBrush UpBrush = new(UpColor);
    private static readonly SolidBrush ConnectedBrush = new(ConnectedColor);
    private static readonly SolidBrush DisconnectedBrush = new(DisconnectedColor);
    private static readonly Pen RowPen = new(RowLine, 1f);


    private IReadOnlyList<IfaceInfo> _items = Array.Empty<IfaceInfo>();
    private IReadOnlyDictionary<string, DayTotal> _today = new Dictionary<string, DayTotal>();
    private int _rowHeight = 46;
    private int _headerHeight = 30;
    private Font? _boldFont;

    public IReadOnlyList<IfaceInfo> Items => _items;

    /// <summary>Bold font shared by every row - created once instead of on every paint.</summary>
    private Font BoldFont => _boldFont ??= new Font(Font, FontStyle.Bold);

    public InterfaceGridControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Color.White;
        AutoScroll = true;
        Font = new Font("Segoe UI", 9f);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _boldFont?.Dispose();
            _boldFont = null;
        }

        base.Dispose(disposing);
    }

    public void SetData(IReadOnlyList<IfaceInfo> items, IReadOnlyDictionary<string, DayTotal> today)
    {
        bool changed = HasChanged(items, today);
        _items = items;
        _today = today;
        UpdateScrollSize();
        if (changed && !DebugFlags.NoPaintGrid) Invalidate();
    }

    /// <summary>
    /// The sampler hands out fresh objects every second, so repaint only when the values
    /// that are actually drawn have changed. An idle interface list then costs nothing.
    /// </summary>
    private bool HasChanged(IReadOnlyList<IfaceInfo> items, IReadOnlyDictionary<string, DayTotal> today)
    {
        if (!ReferenceEquals(today, _today)) return true;
        if (items.Count != _items.Count) return true;

        for (int i = 0; i < items.Count; i++)
        {
            var a = items[i];
            var b = _items[i];
            if (ReferenceEquals(a, b)) continue;
            if (a.Id != b.Id || a.RxRate != b.RxRate || a.TxRate != b.TxRate ||
                a.SessionRx != b.SessionRx || a.SessionTx != b.SessionTx ||
                a.Speed != b.Speed || a.Connected != b.Connected ||
                a.Name != b.Name)
                return true;
        }

        return false;
    }

    private void UpdateScrollSize()
    {
        int needed = _headerHeight + _items.Count * _rowHeight + 4;
        AutoScrollMinSize = new Size(0, Math.Max(0, needed - ClientSize.Height));
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        _boldFont?.Dispose();
        _boldFont = null;
        _rowHeight = Math.Max(44, Font.Height * 2 + 12);
        _headerHeight = Math.Max(26, Font.Height + 14);
        UpdateScrollSize();
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateScrollSize();
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        // fully painted in OnPaint
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);

        int width = ClientSize.Width - (VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0);
        if (width < 50) width = ClientSize.Width;

        var cols = Columns(width);
        DrawHeader(g, width, cols);

        if (_items.Count == 0)
        {
            DrawEmpty(g, width);
            return;
        }

        long maxRate = 1;
        foreach (var it in _items)
        {
            maxRate = Math.Max(maxRate, it.RxRate);
            maxRate = Math.Max(maxRate, it.TxRate);
        }

        int y = _headerHeight;
        for (int i = 0; i < _items.Count; i++)
        {
            DrawRow(g, _items[i], i, y, width, maxRate, cols);
            y += _rowHeight;
        }
    }

    /// <summary>Left gutter for the status block: 6 px margin on each side.</summary>
    private int StatusBlockHeight => Math.Max(8, Font.Height - 5);

    private int StatusBlockWidth => Math.Max(16, StatusBlockHeight * 2);

    private int[] Columns(int width)
    {
        // name | down | up | today | session  -> 6 boundaries
        int x0 = 8 + StatusBlockWidth + 6;
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
        TextRenderer.DrawText(g, Loc.T("main.interface"), font, new Rectangle(cols[0], 0, cols[1] - cols[0], _headerHeight),
            HeaderText, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        // right hand columns are right aligned
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

    private void DrawEmpty(Graphics g, int width)
    {
        var rect = new Rectangle(0, _headerHeight, width, ClientSize.Height - _headerHeight);
        TextRenderer.DrawText(g, Loc.T("main.none"), Font, rect, SubColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private void DrawRow(Graphics g, IfaceInfo item, int index, int y, int width, long maxRate, int[] cols)
    {
        int h = _rowHeight;
        if (index % 2 == 1) g.FillRectangle(AltBrush, 0, y, width, h);

        g.DrawLine(RowPen, 0, y + h - 0.5f, width, y + h - 0.5f);

        int textTop = y + 5;
        int textH = Font.Height + 2;

        // ---- name + status ----
        var nameFont = BoldFont;
        TextRenderer.DrawText(g, item.Name, nameFont, new Rectangle(cols[0], textTop, cols[1] - cols[0] - 8, textH),
            NameColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        string status = item.Connected ? Loc.T("main.connected") : Loc.T("main.disconnected");
        string sub = item.Speed > 0 ? $"{status} · {FormatUtil.Rate(item.Speed / 8)}" : status;
        TextRenderer.DrawText(g, sub, Font, new Rectangle(cols[0], textTop + textH, cols[1] - cols[0] - 8, textH),
            SubColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        // status block (short-wide color block, same geometry as the process grid's remove
        // trigger; here it is display-only and shows the connected state)
        int bw = StatusBlockWidth, bh = StatusBlockHeight;
        g.FillRectangle(item.Connected ? ConnectedBrush : DisconnectedBrush,
            6, y + (h - bh) / 2, bw, bh);

        // ---- rates + bars ----
        DrawRateCell(g, item.RxRate, cols[1], cols[2], textTop, textH, y, h, DownBrush, DownColor, maxRate, item.Speed);
        DrawRateCell(g, item.TxRate, cols[2], cols[3], textTop, textH, y, h, UpBrush, UpColor, maxRate, item.Speed);

        // ---- totals ----
        _today.TryGetValue(item.Id, out var today);
        DrawTotalsCell(g, today.Rx, today.Tx, cols[3], cols[4], textTop, textH);
        DrawTotalsCell(g, item.SessionRx, item.SessionTx, cols[4], cols[5], textTop, textH);
    }

    private void DrawRateCell(Graphics g, long rate, int left, int right, int textTop, int textH, int rowTop, int rowH,
        SolidBrush fillBrush, Color textColor, long maxRate, long speed)
    {
        int w = Math.Max(20, right - left - 16);
        TextRenderer.DrawText(g, FormatUtil.Rate(rate), Font, new Rectangle(left, textTop, w, textH),
            textColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        // progress track under the number (plain rectangle: a 5 px bar does not benefit
        // from rounded corners, and GraphicsPath arcs cost more than the whole repaint)
        int barH = 5;
        int barY = textTop + textH + Math.Max(3, (rowH - (textTop - rowTop) - textH - barH) / 3);
        var track = new Rectangle(right - 16 - w, barY, w, barH);
        g.FillRectangle(TrackBrush, track);

        double denom;
        if (speed > 8)
            denom = speed / 8.0; // bps -> bytes/s
        else
            denom = Math.Max(1, maxRate);

        double frac = denom > 0 ? Math.Clamp(rate / denom, 0, 1) : 0;
        if (rate > 0 && frac < 0.02) frac = 0.02; // keep tiny traffic visible
        int fillW = (int)Math.Round(w * frac);
        if (fillW > 0)
        {
            var fill = new Rectangle(track.X, track.Y, Math.Min(fillW, w), track.Height);
            g.FillRectangle(fillBrush, fill);
        }
    }

    private void DrawTotalsCell(Graphics g, long rx, long tx, int left, int right, int textTop, int textH)
    {
        int w = Math.Max(30, right - left - 16);
        // one full text line per value with a 2 px gap - matches the name/status column
        var top = new Rectangle(left, textTop, w, textH);
        var bottom = new Rectangle(left, textTop + textH + 2, w, textH);

        TextRenderer.DrawText(g, "↓ " + FormatUtil.Bytes(rx), Font, top, DownColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, "↑ " + FormatUtil.Bytes(tx), Font, bottom, UpColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }
}
