using System.Globalization;
using System.Windows;
using System.Windows.Media;
using SysWatt.Core.History;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace SysWatt.App.Controls;

public sealed class TelemetryChart : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(nameof(Values), typeof(IEnumerable<HistoryPoint>),
        typeof(TelemetryChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(nameof(Stroke), typeof(Brush),
        typeof(TelemetryChart), new FrameworkPropertyMetadata(Brushes.MediumAquamarine, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(nameof(Unit), typeof(string),
        typeof(TelemetryChart), new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(nameof(Minimum), typeof(double),
        typeof(TelemetryChart), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(nameof(Maximum), typeof(double),
        typeof(TelemetryChart), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable<HistoryPoint>? Values { get => (IEnumerable<HistoryPoint>?)GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var plot = new Rect(46, 12, Math.Max(0, ActualWidth - 60), Math.Max(0, ActualHeight - 40));
        if (plot.Width < 10 || plot.Height < 10) return;

        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(80, 73, 93, 105)), 1);
        gridPen.Freeze();
        for (var line = 0; line <= 4; line++)
        {
            var y = plot.Top + plot.Height * line / 4d;
            dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
        }
        for (var line = 0; line <= 5; line++)
        {
            var x = plot.Left + plot.Width * line / 5d;
            dc.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
        }

        var points = Values?.ToArray() ?? [];
        var valid = points.Where(p => p.Value.HasValue && double.IsFinite(p.Value.Value)).ToArray();
        var min = double.IsNaN(Minimum) ? (valid.Length == 0 ? 0 : valid.Min(p => p.Value!.Value)) : Minimum;
        var max = double.IsNaN(Maximum) ? (valid.Length == 0 ? 1 : valid.Max(p => p.Value!.Value)) : Maximum;
        if (Math.Abs(max - min) < 0.001) { min = Math.Max(0, min - 1); max += 1; }
        if (double.IsNaN(Minimum)) min = Math.Min(0, min);
        if (double.IsNaN(Maximum)) max *= 1.08;

        DrawLabel(dc, $"{max:0.#}{Unit}", new Point(0, plot.Top - 7), dpi);
        DrawLabel(dc, $"{min:0.#}{Unit}", new Point(0, plot.Bottom - 7), dpi);
        if (points.Length > 0)
        {
            DrawLabel(dc, points[0].Timestamp.ToLocalTime().ToString("HH:mm"), new Point(plot.Left, plot.Bottom + 8), dpi);
            var end = Format(points[^1].Timestamp.ToLocalTime().ToString("HH:mm"), dpi);
            dc.DrawText(end, new Point(plot.Right - end.Width, plot.Bottom + 8));
        }
        if (valid.Length < 2) return;

        var lineGeometry = new StreamGeometry();
        var areaGeometry = new StreamGeometry();
        using (var lineContext = lineGeometry.Open())
        using (var areaContext = areaGeometry.Open())
        {
            var started = false;
            Point first = default;
            Point last = default;
            for (var i = 0; i < points.Length; i++)
            {
                if (!points[i].Value.HasValue || !double.IsFinite(points[i].Value!.Value)) { started = false; continue; }
                var point = new Point(plot.Left + i * plot.Width / Math.Max(1, points.Length - 1),
                    plot.Bottom - Math.Clamp((points[i].Value!.Value - min) / (max - min), 0, 1) * plot.Height);
                if (!started)
                {
                    lineContext.BeginFigure(point, false, false);
                    areaContext.BeginFigure(new Point(point.X, plot.Bottom), true, true);
                    areaContext.LineTo(point, true, false);
                    first = point;
                    started = true;
                }
                else
                {
                    lineContext.LineTo(point, true, false);
                    areaContext.LineTo(point, true, false);
                }
                last = point;
            }
            if (started)
            {
                areaContext.LineTo(new Point(last.X, plot.Bottom), true, false);
                areaContext.LineTo(new Point(first.X, plot.Bottom), true, false);
            }
        }
        lineGeometry.Freeze(); areaGeometry.Freeze();
        var fill = Stroke.Clone(); fill.Opacity = 0.10; fill.Freeze();
        var pen = new Pen(Stroke, 2) { LineJoin = PenLineJoin.Round };
        dc.DrawGeometry(fill, null, areaGeometry);
        dc.DrawGeometry(null, pen, lineGeometry);
        var latest = valid[^1];
        var latestIndex = Array.IndexOf(points, latest);
        var latestPoint = new Point(plot.Left + latestIndex * plot.Width / Math.Max(1, points.Length - 1),
            plot.Bottom - Math.Clamp((latest.Value!.Value - min) / (max - min), 0, 1) * plot.Height);
        dc.DrawEllipse(Stroke, new Pen(new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)), 1), latestPoint, 3.5, 3.5);
    }

    private static void DrawLabel(DrawingContext dc, string text, Point point, double dpi) => dc.DrawText(Format(text, dpi), point);

    private static FormattedText Format(string text, double dpi) => new(text, CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight,
        new Typeface("Segoe UI"), 10, new SolidColorBrush(Color.FromRgb(137, 156, 168)), dpi);
}
