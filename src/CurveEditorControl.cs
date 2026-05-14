namespace SolarMonitorBrightness;

internal sealed class CurveEditorControl : Control
{
    private readonly List<CurvePoint> _points = [];
    private int _selectedIndex = -1;
    private bool _dragging;
    private decimal _referenceLux = 40000;

    public event EventHandler? PointsChanged;
    public event EventHandler? SelectedPointChanged;

    public CurveEditorControl()
    {
        DoubleBuffered = true;
        BackColor = SystemColors.Control;
        ForeColor = SystemColors.ControlText;
        MinimumSize = new Size(560, 300);
        SetPoints(BrightnessCurve.CreateDefault().Points);
    }

    public IReadOnlyList<CurvePoint> Points => _points;

    public CurvePoint? SelectedPoint =>
        _selectedIndex >= 0 && _selectedIndex < _points.Count ? _points[_selectedIndex] : null;

    public decimal ReferenceLux
    {
        get => _referenceLux;
        set
        {
            _referenceLux = Math.Clamp(value, 1, 1000000);
            Normalize();
            Invalidate();
        }
    }

    public void SetPoints(IEnumerable<CurvePoint> points)
    {
        _points.Clear();
        _points.AddRange(points.Select(point => point.Clone()));
        Normalize();
        _selectedIndex = _points.Count > 0 ? 0 : -1;
        Invalidate();
        SelectedPointChanged?.Invoke(this, EventArgs.Empty);
    }

    public BrightnessCurve ToCurve()
    {
        var curve = new BrightnessCurve { Points = _points.Select(point => point.Clone()).ToList() };
        curve.Normalize();
        return curve;
    }

    public void UpdateSelected(decimal lux, int brightness)
    {
        if (SelectedPoint is not { } point)
        {
            return;
        }

        point.Lux = EffectiveLuxToPercent(lux);
        point.Brightness = Math.Clamp(brightness, 1, 100);
        Normalize(keepPoint: point);
        PointsChanged?.Invoke(this, EventArgs.Empty);
        SelectedPointChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public void RemoveSelectedPoint()
    {
        if (_points.Count <= 2 || _selectedIndex < 0)
        {
            return;
        }

        _points.RemoveAt(_selectedIndex);
        _selectedIndex = Math.Clamp(_selectedIndex, 0, _points.Count - 1);
        PointsChanged?.Invoke(this, EventArgs.Empty);
        SelectedPointChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var plot = GetPlotArea();
        using var plotBrush = new SolidBrush(Color.White);
        using var gridPen = new Pen(Color.FromArgb(214, 219, 224));
        using var axisPen = new Pen(Color.FromArgb(120, 130, 140));
        using var linePen = new Pen(Color.FromArgb(0, 120, 215), 2);
        using var extensionPen = new Pen(Color.FromArgb(0, 120, 215), 2);
        using var pointBrush = new SolidBrush(Color.White);
        using var selectedBrush = new SolidBrush(Color.FromArgb(0, 120, 215));
        using var pointBorderPen = new Pen(Color.FromArgb(0, 120, 215), 2);
        using var selectedBorderPen = new Pen(Color.FromArgb(255, 128, 0), 3);
        using var textBrush = new SolidBrush(ForeColor);

        graphics.Clear(BackColor);
        graphics.FillRectangle(plotBrush, plot);

        for (var index = 0; index <= 10; index++)
        {
            var x = plot.Left + index * plot.Width / 10f;
            var y = plot.Top + index * plot.Height / 10f;
            graphics.DrawLine(gridPen, x, plot.Top, x, plot.Bottom);
            graphics.DrawLine(gridPen, plot.Left, y, plot.Right, y);

            if (index > 0)
            {
                var luxLabel = Math.Round(ReferenceLux * index / 10m);
                DrawBottomLabel(graphics, FormatSi(luxLabel), textBrush, x, plot, index);
                graphics.DrawString((100 - index * 10).ToString(), Font, textBrush, plot.Left + 4, y + 2);
            }
        }

        graphics.DrawString("100%", Font, textBrush, plot.Left + 4, plot.Top + 4);
        graphics.DrawString("Lux", Font, textBrush, plot.Right - 28, plot.Top - 20);
        graphics.DrawString("%", Font, textBrush, plot.Left + 4, plot.Top - 20);
        graphics.DrawRectangle(axisPen, Rectangle.Round(plot));

        if (_points.Count > 0)
        {
            var first = ToScreen(_points[0]);
            var last = ToScreen(_points[^1]);
            graphics.DrawLine(extensionPen, plot.Left, first.Y, first.X, first.Y);
            if (_points.Count > 1)
            {
                var screenPoints = _points.Select(ToScreen).ToArray();
                graphics.DrawLines(linePen, screenPoints);
            }

            graphics.DrawLine(extensionPen, last.X, last.Y, plot.Right, last.Y);
        }

        for (var index = 0; index < _points.Count; index++)
        {
            var screenPoint = ToScreen(_points[index]);
            var selected = index == _selectedIndex;
            var radius = selected ? 8 : 5;
            var brush = index == _selectedIndex ? selectedBrush : pointBrush;
            graphics.FillEllipse(brush, screenPoint.X - radius, screenPoint.Y - radius, radius * 2, radius * 2);
            graphics.DrawEllipse(selected ? selectedBorderPen : pointBorderPen, screenPoint.X - radius, screenPoint.Y - radius, radius * 2, radius * 2);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        var hit = HitTest(e.Location);
        if (hit >= 0)
        {
            _selectedIndex = hit;
            _dragging = true;
            SelectedPointChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging || SelectedPoint is not { } point)
        {
            return;
        }

        var value = FromScreen(e.Location);
        point.Lux = value.Lux;
        point.Brightness = value.Brightness;
        Normalize(keepPoint: point);
        PointsChanged?.Invoke(this, EventArgs.Empty);
        SelectedPointChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
    }

    public void AddPoint()
    {
        if (_points.Count >= 8)
        {
            return;
        }

        var value = CreatePointBetweenSelectionAndNeighbor();
        _points.Add(value);
        Normalize(keepPoint: value);
        PointsChanged?.Invoke(this, EventArgs.Empty);
        SelectedPointChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    private int HitTest(Point location)
    {
        for (var index = 0; index < _points.Count; index++)
        {
            var point = ToScreen(_points[index]);
            if (Math.Abs(point.X - location.X) <= 10 && Math.Abs(point.Y - location.Y) <= 10)
            {
                return index;
            }
        }

        return -1;
    }

    private RectangleF GetPlotArea()
    {
        return new RectangleF(44, 28, Width - 72, Height - 58);
    }

    private void DrawBottomLabel(Graphics graphics, string text, Brush brush, float x, RectangleF plot, int index)
    {
        var size = graphics.MeasureString(text, Font);
        var labelX = index == 10
            ? plot.Right - size.Width - 2
            : x - size.Width / 2;
        labelX = Math.Clamp(labelX, plot.Left + 2, plot.Right - size.Width - 2);
        graphics.DrawString(text, Font, brush, labelX, plot.Bottom - 22);
    }

    private PointF ToScreen(CurvePoint point)
    {
        var plot = GetPlotArea();
        var x = plot.Left + (float)(point.Lux / 100m) * plot.Width;
        var y = plot.Bottom - (point.Brightness / 100f) * plot.Height;
        return new PointF(x, y);
    }

    private CurvePoint FromScreen(Point point)
    {
        var plot = GetPlotArea();
        var x = Math.Clamp(point.X, plot.Left, plot.Right);
        var y = Math.Clamp(point.Y, plot.Top, plot.Bottom);
        var luxPercent = (decimal)((x - plot.Left) / plot.Width) * 100;
        var brightness = (int)Math.Round((plot.Bottom - y) / plot.Height * 100);
        return new CurvePoint
        {
            Lux = Math.Round(luxPercent, 2),
            Brightness = Math.Clamp(brightness, 1, 100)
        };
    }

    private CurvePoint CreatePointBetweenSelectionAndNeighbor()
    {
        if (_points.Count == 0)
        {
            return new CurvePoint { Lux = 50, Brightness = 50 };
        }

        var lux = 50m;
        var brightness = GetBrightnessAtPercent(lux);

        if (_points.Any(point => point.Lux == lux))
        {
            lux = FindFreeLux(lux);
        }

        return new CurvePoint
        {
            Lux = Math.Round(lux),
            Brightness = Math.Clamp(brightness, 1, 100)
        };
    }

    private decimal FindFreeLux(decimal preferredLux)
    {
        var step = 1m;
        for (var multiplier = 1; multiplier <= 100; multiplier++)
        {
            var right = Math.Clamp(preferredLux + step * multiplier, 0, 100);
            if (_points.All(point => point.Lux != right))
            {
                return right;
            }

            var left = Math.Clamp(preferredLux - step * multiplier, 0, 100);
            if (_points.All(point => point.Lux != left))
            {
                return left;
            }
        }

        return preferredLux;
    }

    public decimal GetSelectedEffectiveLux()
    {
        return SelectedPoint is { } point ? PercentToEffectiveLux(point.Lux) : 0;
    }

    private decimal PercentToEffectiveLux(decimal luxPercent)
    {
        return Math.Round(Math.Clamp(luxPercent, 0, 100) / 100 * ReferenceLux);
    }

    private decimal EffectiveLuxToPercent(decimal effectiveLux)
    {
        return Math.Clamp(effectiveLux / ReferenceLux * 100, 0, 100);
    }

    private int GetBrightnessAtPercent(decimal luxPercent)
    {
        var curve = ToCurve();
        return BrightnessMapper.MapLuxToBrightness(luxPercent, curve, 100);
    }

    private static string FormatSi(decimal value)
    {
        if (value >= 1000000)
        {
            return $"{value / 1000000:0.#}M";
        }

        if (value >= 1000)
        {
            return $"{value / 1000:0.#}k";
        }

        return value.ToString("0");
    }

    private void Normalize(CurvePoint? keepPoint = null)
    {
        foreach (var point in _points)
        {
            point.Lux = Math.Clamp(point.Lux, 0, 100);
            point.Brightness = Math.Clamp(point.Brightness, 1, 100);
        }

        _points.Sort((left, right) => left.Lux.CompareTo(right.Lux));
        if (keepPoint is not null)
        {
            _selectedIndex = _points.IndexOf(keepPoint);
        }
    }
}
