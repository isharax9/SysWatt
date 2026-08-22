using System.Windows;
using System.Windows.Media;
using SysWatt.Core.History;

namespace SysWatt.App.Controls;

public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(nameof(Values),
        typeof(IEnumerable<HistoryPoint>), typeof(Sparkline), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(nameof(Stroke),
        typeof(System.Windows.Media.Brush), typeof(Sparkline), new FrameworkPropertyMetadata(System.Windows.Media.Brushes.MediumAquamarine, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable<HistoryPoint>? Values { get => (IEnumerable<HistoryPoint>?)GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public System.Windows.Media.Brush Stroke { get => (System.Windows.Media.Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var points = Values?.ToArray() ?? [];
        var valid = points.Where(p => p.Value.HasValue).Select(p => p.Value!.Value).ToArray();
        if (points.Length < 2 || valid.Length < 2 || ActualWidth <= 0 || ActualHeight <= 0) return;
        var min = valid.Min();
        var max = valid.Max();
        if (Math.Abs(max - min) < 0.001) { min -= 1; max += 1; }
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            var started = false;
            for (var i = 0; i < points.Length; i++)
            {
                if (!points[i].Value.HasValue) { started = false; continue; }
                var x = i * ActualWidth / Math.Max(1, points.Length - 1);
                var y = ActualHeight - ((points[i].Value!.Value - min) / (max - min) * ActualHeight);
                if (!started) { context.BeginFigure(new System.Windows.Point(x, y), false, false); started = true; }
                else context.LineTo(new System.Windows.Point(x, y), true, false);
            }
        }
        geometry.Freeze();
        drawingContext.DrawGeometry(null, new System.Windows.Media.Pen(Stroke, 1.5), geometry);
    }
}
