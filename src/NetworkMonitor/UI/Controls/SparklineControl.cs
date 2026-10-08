using NetworkMonitor.Core;

namespace NetworkMonitor.UI.Controls;

/// <summary>
/// Minimal two-series sparkline (download / upload) drawn with GDI+.
/// Keeps a fixed-size ring buffer so it never allocates after construction.
/// </summary>
public sealed class SparklineControl : Control
{
    private float[] _down = new float[120];
    private float[] _up = new float[120];
    private int _capacity = 120;
    private int _count;
    private int _head;

    public Color DownColor { get; set; } = Color.FromArgb(77, 182, 255);
    public Color UpColor { get; set; } = Color.FromArgb(255, 176, 92);
    public Color BaselineColor { get; set; } = Color.FromArgb(60, 255, 255, 255);

    public SparklineControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    public int Capacity
    {
        get => _capacity;
        set
        {
            value = Math.Max(2, value);
            if (value == _capacity) return;
            _capacity = value;
            _down = new float[value];
            _up = new float[value];
            _count = 0;
            _head = 0;
        }
    }

    public void AddSample(float down, float up)
    {
        _down[_head] = down;
        _up[_head] = up;
        _head = (_head + 1) % _capacity;
        if (_count < _capacity) _count++;
        if (!DebugFlags.NoPaint) Invalidate();
    }

    public void Clear()
    {
        _count = 0;
        _head = 0;
        Array.Clear(_down);
        Array.Clear(_up);
        Invalidate();
    }

    private float At(float[] src, int index)
    {
        // index 0 = oldest sample
        int start = (_head - _count + _capacity) % _capacity;
        return src[(start + index) % _capacity];
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        int w = ClientSize.Width, h = ClientSize.Height;
        if (w < 4 || h < 4 || _count < 2) return;

        float max = 0.0001f;
        for (int i = 0; i < _count; i++)
        {
            max = Math.Max(max, At(_down, i));
            max = Math.Max(max, At(_up, i));
        }

        // gentle head-room so the line does not touch the top edge
        max *= 1.15f;

        using var baseline = new Pen(BaselineColor, 1f);
        g.DrawLine(baseline, 0, h - 1, w, h - 1);

        DrawSeries(g, _down, w, h, max, DownColor);
        DrawSeries(g, _up, w, h, max, UpColor);
    }

    private void DrawSeries(Graphics g, float[] data, int w, int h, float max, Color color)
    {
        if (_count < 2) return;
        int n = _count;
        float dx = n == 1 ? 0 : (float)w / (n - 1);

        using var pen = new Pen(color, 1.6f)
        {
            LineJoin = System.Drawing.Drawing2D.LineJoin.Round,
        };

        PointF prev = default;
        for (int i = 0; i < n; i++)
        {
            float v = At(data, i);
            float y = h - 2 - (float)(v / max) * (h - 4);
            var p = new PointF(i * dx, Math.Clamp(y, 1f, h - 2f));
            if (i > 0) g.DrawLine(pen, prev, p);
            prev = p;
        }

        // marker on the most recent sample
        using var dot = new SolidBrush(color);
        g.FillEllipse(dot, prev.X - 2.2f, prev.Y - 2.2f, 4.4f, 4.4f);
    }
}
