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
    public static readonly DependencyProperty SecondaryValuesProperty = DependencyProperty.Register(nameof(SecondaryValues), typeof(IEnumerable<HistoryPoint>),
        typeof(TelemetryChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SecondaryStrokeProperty = DependencyProperty.Register(nameof(SecondaryStroke), typeof(Brush),
        typeof(TelemetryChart), new FrameworkPropertyMetadata(Brushes.CornflowerBlue, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SecondaryUnitProperty = DependencyProperty.Register(nameof(SecondaryUnit), typeof(string),
        typeof(TelemetryChart), new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SecondaryMinimumProperty = DependencyProperty.Register(nameof(SecondaryMinimum), typeof(double),
        typeof(TelemetryChart), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SecondaryMaximumProperty = DependencyProperty.Register(nameof(SecondaryMaximum), typeof(double),
        typeof(TelemetryChart), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable<HistoryPoint>? Values { get => (IEnumerable<HistoryPoint>?)GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public IEnumerable<HistoryPoint>? SecondaryValues { get => (IEnumerable<HistoryPoint>?)GetValue(SecondaryValuesProperty); set => SetValue(SecondaryValuesProperty, value); }
    public Brush SecondaryStroke { get => (Brush)GetValue(SecondaryStrokeProperty); set => SetValue(SecondaryStrokeProperty, value); }
    public string SecondaryUnit { get => (string)GetValue(SecondaryUnitProperty); set => SetValue(SecondaryUnitProperty, value); }
    public double SecondaryMinimum { get => (double)GetValue(SecondaryMinimumProperty); set => SetValue(SecondaryMinimumProperty, value); }
    public double SecondaryMaximum { get => (double)GetValue(SecondaryMaximumProperty); set => SetValue(SecondaryMaximumProperty, value); }

    private static readonly Typeface LabelTypeface = new("Segoe UI");
    private static readonly Brush LabelBrush = new SolidColorBrush(Color.FromRgb(137, 156, 168));

    static TelemetryChart()
    {
        LabelBrush.Freeze();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (!IsVisible) return;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var hasSecondary = SecondaryValues is not null;
        var plot = new Rect(50, 12, Math.Max(0, ActualWidth - (hasSecondary ? 104 : 66)), Math.Max(0, ActualHeight - 40));
        if (plot.Width < 10 || plot.Height < 10) return;

        var gridBrush = TryFindResource("ChartGridBrush") as Brush ?? new SolidColorBrush(Color.FromArgb(80, 73, 93, 105));
        var gridPen = new Pen(gridBrush, 1);
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

        var points = Values as IReadOnlyList<HistoryPoint> ?? Values?.ToList() ?? (IReadOnlyList<HistoryPoint>)[];
        var (min, max, countValid) = Scale(points, Minimum, Maximum);
        var primaryHasData = countValid > 0;
        DrawLabel(dc, primaryHasData ? $"{max:0.#}{Unit}" : "N/A", new Point(0, plot.Top - 7), dpi);
        DrawLabel(dc, $"{min:0.#}{Unit}", new Point(0, plot.Bottom - 7), dpi);
        if (points.Count > 0)
        {
            DrawLabel(dc, points[0].Timestamp.ToLocalTime().ToString("HH:mm"), new Point(plot.Left, plot.Bottom + 8), dpi);
            var end = Format(points[^1].Timestamp.ToLocalTime().ToString("HH:mm"), dpi);
            dc.DrawText(end, new Point(plot.Right - end.Width, plot.Bottom + 8));
        }
        DrawSeries(dc, points, plot, min, max, Stroke, true);

        var secondary = SecondaryValues as IReadOnlyList<HistoryPoint> ?? SecondaryValues?.ToList() ?? (IReadOnlyList<HistoryPoint>)[];
        if (secondary.Count > 0)
        {
            var (secondaryMin, secondaryMax, secondaryCountValid) = Scale(secondary, SecondaryMinimum, SecondaryMaximum);
            var top = Format(secondaryCountValid > 0 ? $"{secondaryMax:0.#}{SecondaryUnit}" : "N/A", dpi);
            var bottom = Format($"{secondaryMin:0.#}{SecondaryUnit}", dpi);
            dc.DrawText(top, new Point(plot.Right + 8, plot.Top - 7));
            dc.DrawText(bottom, new Point(plot.Right + 8, plot.Bottom - 7));
            DrawSeries(dc, secondary, plot, secondaryMin, secondaryMax, SecondaryStroke, false);
        }
    }

    private static (double Minimum, double Maximum, int ValidCount) Scale(IReadOnlyList<HistoryPoint> points, double requestedMinimum, double requestedMaximum)
    {
        var min = double.MaxValue;
        var max = double.MinValue;
        var validCount = 0;
        for (var i = 0; i < points.Count; i++)
        {
            var v = points[i].Value;
            if (!v.HasValue || !double.IsFinite(v.Value)) continue;
            validCount++;
            if (v.Value < min) min = v.Value;
            if (v.Value > max) max = v.Value;
        }

        if (validCount == 0)
        {
            min = double.IsNaN(requestedMinimum) ? 0 : requestedMinimum;
            max = double.IsNaN(requestedMaximum) ? 1 : requestedMaximum;
        }
        else
        {
            if (!double.IsNaN(requestedMinimum)) min = requestedMinimum;
            if (!double.IsNaN(requestedMaximum)) max = requestedMaximum;
        }

        if (Math.Abs(max - min) < 0.001) { min = Math.Max(0, min - 1); max += 1; }
        if (double.IsNaN(requestedMinimum)) min = Math.Min(0, min);
        if (double.IsNaN(requestedMaximum)) max *= 1.08;
        return (min, max, validCount);
    }

    private static void DrawSeries(DrawingContext dc, IReadOnlyList<HistoryPoint> points, Rect plot, double min, double max, Brush stroke, bool fillArea)
    {
        if (points.Count < 2) return;
        var lineGeometry = new StreamGeometry();
        var areaGeometry = new StreamGeometry();
        var validCount = 0;
        Point lastValidPoint = default;

        using (var lineContext = lineGeometry.Open())
        using (var areaContext = areaGeometry.Open())
        {
            var started = false;
            Point first = default;
            Point last = default;
            for (var i = 0; i < points.Count; i++)
            {
                var v = points[i].Value;
                if (!v.HasValue || !double.IsFinite(v.Value)) { started = false; continue; }
                validCount++;
                var point = new Point(plot.Left + i * plot.Width / Math.Max(1, points.Count - 1),
                    plot.Bottom - Math.Clamp((v.Value - min) / (max - min), 0, 1) * plot.Height);
                lastValidPoint = point;

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

        if (validCount < 2) return;
        lineGeometry.Freeze(); areaGeometry.Freeze();
        if (fillArea)
        {
            var fill = stroke.Clone(); fill.Opacity = 0.09; fill.Freeze();
            dc.DrawGeometry(fill, null, areaGeometry);
        }
        var pen = new Pen(stroke, 2) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        dc.DrawGeometry(null, pen, lineGeometry);
        dc.DrawEllipse(stroke, null, lastValidPoint, 3.5, 3.5);
    }

    private static void DrawLabel(DrawingContext dc, string text, Point point, double dpi) => dc.DrawText(Format(text, dpi), point);

    private static FormattedText Format(string text, double dpi) => new(text, CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight,
        LabelTypeface, 10, LabelBrush, dpi);
}
