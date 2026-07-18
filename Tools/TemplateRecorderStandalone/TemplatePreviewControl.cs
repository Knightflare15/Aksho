using System.Drawing.Drawing2D;

namespace TemplateRecorderStandalone;

public sealed class TemplatePreviewControl : Control
{
    List<TemplateStore.RawPoint> _points = new();

    public TemplatePreviewControl()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(246, 239, 220);
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint, true);
    }

    public float StrokeWidth { get; set; } = 5f;

    public void SetTemplate(TemplateStore.TemplateEntry? entry)
    {
        _points = entry?.points?.ToList() ?? new List<TemplateStore.RawPoint>();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var backgroundBrush = new SolidBrush(BackColor);
        e.Graphics.FillRectangle(backgroundBrush, ClientRectangle);
        using var borderPen = new Pen(Color.FromArgb(196, 172, 130), 1.5f);
        e.Graphics.DrawRectangle(borderPen, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));

        if (_points.Count == 0)
            return;

        float minX = _points.Min(point => point.x);
        float maxX = _points.Max(point => point.x);
        float minY = _points.Min(point => point.y);
        float maxY = _points.Max(point => point.y);

        float width = Math.Max(1f, maxX - minX);
        float height = Math.Max(1f, maxY - minY);
        float padding = 12f;
        float availableWidth = Math.Max(1f, Width - (padding * 2f));
        float availableHeight = Math.Max(1f, Height - (padding * 2f));
        float scale = Math.Min(availableWidth / width, availableHeight / height);
        float offsetX = (Width - (width * scale)) * 0.5f;
        float offsetY = (Height - (height * scale)) * 0.5f;

        using var strokePen = new Pen(Color.FromArgb(46, 89, 154), StrokeWidth)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        using var dotBrush = new SolidBrush(Color.FromArgb(46, 89, 154));

        var grouped = _points.GroupBy(point => point.strokeId);
        foreach (IGrouping<int, TemplateStore.RawPoint> stroke in grouped)
        {
            List<PointF> path = stroke
                .Select(point => new PointF(
                    offsetX + ((point.x - minX) * scale),
                    offsetY + ((point.y - minY) * scale)))
                .ToList();

            if (path.Count == 1)
            {
                float radius = StrokeWidth * 0.5f;
                e.Graphics.FillEllipse(dotBrush, path[0].X - radius, path[0].Y - radius, radius * 2f, radius * 2f);
                continue;
            }

            for (int i = 1; i < path.Count; i++)
                e.Graphics.DrawLine(strokePen, path[i - 1], path[i]);
        }
    }
}
