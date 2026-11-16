using System.Globalization;
using System.Windows;
using System.Windows.Media;
using HabitTracker.Core;

namespace HabitTracker.Controls;

public sealed class VolumeChart : FrameworkElement
{
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(nameof(Points), typeof(IReadOnlyList<VolumePoint>), typeof(VolumeChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyList<VolumePoint>? Points { get => (IReadOnlyList<VolumePoint>?)GetValue(PointsProperty); set => SetValue(PointsProperty, value); }
    public VolumeChart() => SizeChanged += (_, _) => InvalidateVisual();
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (Points == null || Points.Count == 0 || ActualWidth < 100 || ActualHeight < 80) return;
        const double left = 48, top = 14, bottom = 30;
        var chartWidth = ActualWidth - left - 8; var chartHeight = ActualHeight - top - bottom;
        var max = Math.Max(1000, Math.Ceiling((double)Points.Max(p => p.Volume) / 1000) * 1000);
        for (var i = 0; i <= 3; i++)
        {
            var y = top + chartHeight * i / 3;
            dc.DrawLine(new Pen(Brush("#E9EEE6"), 1), new Point(left, y), new Point(ActualWidth - 8, y));
            Text(dc, (max * (3 - i) / 3 / 1000).ToString("0.#", CultureInfo.InvariantCulture) + "k", 0, y - 7, "#7D8B7C", 10);
        }
        var step = chartWidth / Points.Count; var barWidth = Math.Min(42, step * .55);
        for (var i = 0; i < Points.Count; i++)
        {
            var x = left + step * i + (step - barWidth) / 2;
            var h = chartHeight * (double)Points[i].Volume / max;
            dc.DrawRoundedRectangle(Brush(i == Points.Count - 1 ? "#C9ED7C" : "#71966B"), null,
                new Rect(x, top + chartHeight - Math.Max(2, h), barWidth, Math.Max(2, h)), 4, 4);
            Text(dc, Points[i].Label, x + barWidth / 2, top + chartHeight + 13, "#7D8B7C", 10, true);
        }
    }
    private static Brush Brush(string color) => (Brush)new BrushConverter().ConvertFromString(color)!;
    private void Text(DrawingContext dc, string text, double x, double y, string color, double size, bool center = false)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, Brush(color), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(formatted, new Point(center ? x - formatted.Width / 2 : x, y));
    }
}
