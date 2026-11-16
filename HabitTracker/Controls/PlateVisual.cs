using System.Globalization;
using System.Windows;
using System.Windows.Media;
using HabitTracker.Core;

namespace HabitTracker.Controls;

/// <summary>Resolution-independent visualization of the actual saved sleeve stacks.</summary>
public sealed class PlateVisual : FrameworkElement
{
    public static readonly DependencyProperty LoadProperty = DependencyProperty.Register(nameof(Load), typeof(PlateLoad), typeof(PlateVisual), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public PlateLoad? Load { get => (PlateLoad?)GetValue(LoadProperty); set => SetValue(LoadProperty, value); }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var w = ActualWidth; var y = ActualHeight / 2;
        if (w < 40 || ActualHeight < 40) return;
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(160, 177, 164)), null, new Rect(12, y - 5, w - 24, 10), 3, 3);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(206, 216, 201)), null, new Rect(w * .35, y - 7, w * .30, 14), 3, 3);
        if (Load == null) return;
        DrawStack(dc, Load.LeftPlates, w * .32, y, -1);
        DrawStack(dc, Load.RightPlates, w * .68, y, 1);
        Text(dc, $"{PlateLoad.Format(Load.BarWeight)} {Load.Symbol} bar", w / 2, y + 32, 11, "#6D7F70");
    }
    private void DrawStack(DrawingContext dc, IReadOnlyList<decimal> plates, double start, double center, int direction)
    {
        var maxWidth = Math.Max(10, ActualWidth * .26 / Math.Max(plates.Count, 1));
        var width = Math.Min(27, maxWidth - 3);
        for (var i = 0; i < plates.Count; i++)
        {
            var weight = plates[i];
            var height = Math.Clamp(44 + (double)weight * 1.15, 40, Math.Max(40, ActualHeight - 25));
            var x = direction < 0 ? start - (i + 1) * (width + 3) : start + i * (width + 3);
            var color = weight >= 40 ? "#24523C" : weight >= 20 ? "#81A467" : weight >= 10 ? "#BBD681" : "#D6E8B9";
            dc.DrawRoundedRectangle((Brush)new BrushConverter().ConvertFromString(color)!, null, new Rect(x, center - height / 2, width, height), 4, 4);
            if (width >= 19) Text(dc, PlateLoad.Format(weight), x + width / 2, center - 8, 10, weight >= 40 ? "#FFFFFF" : "#23402B");
        }
    }
    private void Text(DrawingContext dc, string text, double centerX, double y, double size, string color)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), size, (Brush)new BrushConverter().ConvertFromString(color)!, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(formatted, new Point(centerX - formatted.Width / 2, y));
    }
}
