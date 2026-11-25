using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using HabitTracker.Core;

namespace HabitTracker.Mobile.Controls;

/// <summary>OpenSilver primitives replace the desktop-only DrawingContext control.</summary>
public sealed class BarbellVisual : Canvas
{
    public static readonly DependencyProperty LoadProperty = DependencyProperty.Register(nameof(Load), typeof(PlateLoad),
        typeof(BarbellVisual), new PropertyMetadata(null, (sender, _) => ((BarbellVisual)sender).Draw()));
    public PlateLoad? Load { get => (PlateLoad?)GetValue(LoadProperty); set => SetValue(LoadProperty, value); }
    public BarbellVisual() { SizeChanged += (_, _) => Draw(); Loaded += (_, _) => Draw(); IsHitTestVisible = false; }

    private static Brush Brush(byte r, byte g, byte b) => new SolidColorBrush(Color.FromArgb(255, r, g, b));
    private void Box(double x, double y, double width, double height, Brush color, double radius = 3)
    {
        var rectangle = new Rectangle { Width = Math.Max(1, width), Height = Math.Max(1, height), Fill = color, RadiusX = radius, RadiusY = radius };
        SetLeft(rectangle, x); SetTop(rectangle, y); Children.Add(rectangle);
    }
    private void Draw()
    {
        Children.Clear();
        if (ActualWidth < 40 || ActualHeight < 30) return;
        var middle = ActualHeight / 2;
        Box(6, middle - 4, ActualWidth - 12, 8, Brush(149, 169, 151));
        Box(ActualWidth * .34, middle - 5, ActualWidth * .32, 10, Brush(193, 207, 183));
        if (Load == null) return;
        Stack(Load.LeftPlates, ActualWidth * .31, middle, -1);
        Stack(Load.RightPlates, ActualWidth * .69, middle, 1);
    }
    private void Stack(IReadOnlyList<decimal> plates, double start, double middle, int direction)
    {
        var width = Math.Min(22, Math.Max(2, ActualWidth * .25 / Math.Max(1, plates.Count) - 3));
        var tallest = Math.Max(30, ActualHeight - 10);
        for (var i = 0; i < plates.Count; i++)
        {
            var weight = plates[i];
            var height = Math.Min(tallest, 26 + (double)weight * .95);
            var x = direction < 0 ? start - (i + 1) * (width + 3) : start + i * (width + 3);
            var color = weight >= 40 ? Brush(32, 77, 53) : weight >= 20 ? Brush(119, 155, 93) : Brush(192, 220, 132);
            Box(x, middle - height / 2, width, height, color, 3);
            if (width < 16) continue;
            var label = new TextBlock { Text = PlateLoad.Format(weight), FontSize = 9, Width = width, TextAlignment = TextAlignment.Center,
                Foreground = weight >= 40 ? Brush(255, 255, 255) : Brush(25, 55, 41) };
            SetLeft(label, x); SetTop(label, middle - 6); Children.Add(label);
        }
    }
}
