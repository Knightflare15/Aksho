using System.Drawing.Drawing2D;

namespace TemplateRecorderStandalone;

public sealed class StrokeCanvasControl : Control
{
    readonly List<List<PointF>> _strokes = new();
    readonly List<List<HandwritingCapturedSeed>> _capturedStrokes = new();
    List<PointF>? _activeStroke;
    List<HandwritingCapturedSeed>? _activeCapturedStroke;
    HandwritingCaptureClock _captureClock = new();

    public event Action? StrokesChanged;

    public StrokeCanvasControl()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(246, 239, 220);
        ForeColor = Color.FromArgb(41, 38, 31);
        Font = new Font("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point);
        Cursor = Cursors.Cross;
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint, true);
    }

    public float StrokeWidth { get; set; } = 8f;

    public string EmptyHint { get; set; } = "Left-click to draw. Right-click to lift pen.";

    public IReadOnlyList<IReadOnlyList<PointF>> GetStrokeCopy()
    {
        var copy = new List<IReadOnlyList<PointF>>(_strokes.Count);
        foreach (List<PointF> stroke in _strokes)
            copy.Add(stroke.ToList());
        return copy;
    }

    public HandwritingSampleRecord BuildSample(string expectedLetter, string writerId, string sessionId)
    {
        var copy = new List<IReadOnlyList<HandwritingCapturedSeed>>(_capturedStrokes.Count);
        foreach (List<HandwritingCapturedSeed> stroke in _capturedStrokes)
            copy.Add(stroke.Select(point => new HandwritingCapturedSeed { position = point.position, tMs = point.tMs }).ToList());
        return HandwritingSampleFactory.Build(
            expectedLetter,
            writerId,
            sessionId,
            copy,
            ClientSize,
            _captureClock.StartedAtUtc);
    }

    public void ClearStrokes()
    {
        _strokes.Clear();
        _capturedStrokes.Clear();
        _activeStroke = null;
        _activeCapturedStroke = null;
        _captureClock = new HandwritingCaptureClock();
        Invalidate();
        StrokesChanged?.Invoke();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button == MouseButtons.Left)
        {
            if (_activeStroke == null || _activeStroke.Count == 0)
            {
                _activeStroke = new List<PointF>();
                _strokes.Add(_activeStroke);
                _activeCapturedStroke = new List<HandwritingCapturedSeed>();
                _capturedStrokes.Add(_activeCapturedStroke);
            }

            AddPoint(e.Location);
            return;
        }

        if (e.Button == MouseButtons.Right)
        {
            _activeStroke = new List<PointF>();
            _strokes.Add(_activeStroke);
            _activeCapturedStroke = new List<HandwritingCapturedSeed>();
            _capturedStrokes.Add(_activeCapturedStroke);
            Invalidate();
            StrokesChanged?.Invoke();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (e.Button != MouseButtons.Left || _activeStroke == null)
            return;

        AddPoint(e.Location);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button == MouseButtons.Left)
            _activeStroke = null;
        if (e.Button == MouseButtons.Left)
            _activeCapturedStroke = null;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var backgroundBrush = new SolidBrush(BackColor);
        e.Graphics.FillRectangle(backgroundBrush, ClientRectangle);

        using var borderPen = new Pen(Color.FromArgb(196, 172, 130), 2f);
        e.Graphics.DrawRectangle(borderPen, 1, 1, Math.Max(0, Width - 2), Math.Max(0, Height - 2));

        if (_strokes.Count == 0 || _strokes.All(stroke => stroke.Count == 0))
        {
            using var hintBrush = new SolidBrush(Color.FromArgb(120, 88, 70));
            using var hintFormat = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            e.Graphics.DrawString(EmptyHint, Font, hintBrush, ClientRectangle, hintFormat);
            return;
        }

        using var strokePen = new Pen(Color.FromArgb(46, 89, 154), StrokeWidth)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        using var dotBrush = new SolidBrush(Color.FromArgb(46, 89, 154));

        foreach (List<PointF> stroke in _strokes)
        {
            if (stroke.Count == 1)
            {
                PointF point = stroke[0];
                float radius = StrokeWidth * 0.5f;
                e.Graphics.FillEllipse(dotBrush, point.X - radius, point.Y - radius, radius * 2f, radius * 2f);
                continue;
            }

            for (int i = 1; i < stroke.Count; i++)
                e.Graphics.DrawLine(strokePen, stroke[i - 1], stroke[i]);
        }
    }

    void AddPoint(Point point)
    {
        if (_activeStroke == null)
            return;

        var value = new PointF(point.X, point.Y);
        if (_activeStroke.Count > 0)
        {
            PointF last = _activeStroke[^1];
            float dx = last.X - value.X;
            float dy = last.Y - value.Y;
            if (((dx * dx) + (dy * dy)) < 4f)
                return;
        }

        _activeStroke.Add(value);
        _activeCapturedStroke?.Add(new HandwritingCapturedSeed
        {
            position = value,
            tMs = _captureClock.ElapsedMilliseconds
        });
        Invalidate();
        StrokesChanged?.Invoke();
    }
}
