using NetworkMonitor.Core;

namespace NetworkMonitor.UI.Controls;

/// <summary>
/// Owner-drawn list of monitored processes - same visual language as the interface grid:
/// a status color block at the row start (doubles as the remove trigger, with hand cursor
/// + hover highlight), live down/up with relative bars, today and session totals.
/// The control is never scrolled itself; the main window hosts it inside an AutoScroll
/// panel and sizes it to the full content height, which avoids the scroll-blit artefacts
/// of scrolling a fully owner-drawn surface.
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

    private static readonly SolidBrush AltBrush = new(RowAlt);
    private static readonly SolidBrush HeaderBrush = new(HeaderBack);
    private static readonly SolidBrush TrackBrush = new(TrackColor);
    private static readonly SolidBrush DownBrush = new(DownColor);
    private static readonly SolidBrush UpBrush = new(UpColor);
    private static readonly SolidBrush RunningBrush = new(RunningColor);
    private static readonly SolidBrush StoppedBrush = new(StoppedColor);
    private static readonly Pen RowPen = new(RowLine, 1f);

    private IReadOnlyList<ProcInfo> _items = Array.Empty<ProcInfo>();
    private IReadOnlyDictionary<string, DayTotal> _today = new Dictionary<string, DayTotal>();
    private int _rowHeight = 46;
    private int _headerHeight = 30;
    private Font? _boldFont;
    private readonly ContextMenuStrip _menu = new();
    private string? _menuTarget;
    private readonly Rectangle[] _blockRects = new Rectangle[8];
    private int _hoverBlock = -1;
    private ToolTip _tooltip = new();

    /// <summary>Rows visible at once inside the hosting AutoScroll panel; the rest scrolls.</summary>
    public const int VisibleRowCap = 2;

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

        _tooltip.SetToolTip(this, Loc.T("proc.removeBlock"));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _boldFont?.Dispose();
            _menu.Dispose();
            _tooltip.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>Full content height (the hosting AutoScroll panel scrolls over it).</summary>
    public int ContentHeight => _headerHeight + Math.Max(1, _items.Count) * _rowHeight + 4;

    /// <summary>Viewport height that shows at most <paramref name="rows"/> rows.</summary>
    public int HeightForRows(int rows) => _headerHeight + Math.Max(1, rows) * _rowHeight + 4;

    /// <summary>Bold font shared by every row - created once instead of on every paint.</summary>
    private Font BoldFont => _boldFont ??= new Font(Font, FontStyle.Bold);

    public void SetData(IReadOnlyList<ProcInfo> items, IReadOnlyDictionary<string, DayTotal> today)
    {
        bool changed = HasChanged(items, today);
        _items = items;
        _today = today;

        // the control always spans the full content; the AutoScroll host provides the viewport
        int h = ContentHeight;
        if (Height != h) Height = h;

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
        Height = ContentHeight;
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

        var cols = Columns(width);
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
        for (int i = 0; i < _items.Count && i < _blockRects.Length; i++)
        {
            DrawRow(g, _items[i], i, y, width, maxRate, cols);
            y += _rowHeight;
        }
    }

    private int BlockHeight => Math.Max(8, Font.Height - 5);

    private int BlockWidth => BlockHeight * 2;

    private int[] Columns(int width)
    {
        // name | down | up | today | session  -> 6 boundaries
        int x0 = 8 + BlockWidth + 6;
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
        int nameRight = cols[1] - 8;
        TextRenderer.DrawText(g, display, BoldFont, new Rectangle(cols[0], textTop, Math.Max(20, nameRight - cols[0]), textH),
            NameColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        string status = item.Running ? Loc.T("proc.running") : Loc.T("proc.notRunning");
        TextRenderer.DrawText(g, status, Font, new Rectangle(cols[0], textTop + textH, Math.Max(20, nameRight - cols[0]), textH),
            SubColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        // ---- status/remove block (row start, short+wide; click removes) ----
        int bw = BlockWidth, bh = BlockHeight;
        var block = new Rectangle(6, y + (h - bh) / 2, bw, bh);
        _blockRects[index] = block;

        bool hovered = index == _hoverBlock;
        var blockColor = item.Running ? RunningColor : StoppedColor;
        if (hovered)
            blockColor = Color.FromArgb(Math.Min(255, blockColor.R + 45), Math.Min(255, blockColor.G + 45), blockColor.B);
        using (var b = new SolidBrush(blockColor))
            g.FillRectangle(b, block);
        if (hovered)
        {
            using var pen = new Pen(Color.FromArgb(190, 60, 60), 1.4f);
            g.DrawRectangle(pen, block.X - 2, block.Y - 2, block.Width + 4, block.Height + 4);
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

    private int RowIndexAt(int contentY)
    {
        if (contentY < _headerHeight) return -1;
        int idx = (contentY - _headerHeight) / _rowHeight;
        return idx >= 0 && idx < _items.Count ? idx : -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var cp = e.Location;
        int idx = RowIndexAt(cp.Y);
        bool onBlock = idx >= 0 && idx < _blockRects.Length && _blockRects[idx].Contains(cp);
        int next = onBlock ? idx : -1;
        if (next != _hoverBlock)
        {
            _hoverBlock = next;
            Invalidate();
        }

        Cursor = onBlock ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverBlock != -1)
        {
            _hoverBlock = -1;
            Invalidate();
        }

        Cursor = Cursors.Default;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        var cp = e.Location;

        if (e.Button == MouseButtons.Left)
        {
            int idx = RowIndexAt(cp.Y);
            if (idx >= 0 && idx < _blockRects.Length && _blockRects[idx].Contains(cp))
            {
                RemoveRequested?.Invoke(_items[idx].Name);
                return;
            }
        }
        else if (e.Button == MouseButtons.Right)
        {
            int idx = RowIndexAt(cp.Y);
            _menuTarget = idx >= 0 ? _items[idx].Name : null;
            if (_menuTarget is not null) _menu.Show(this, e.Location);
        }
    }

}

