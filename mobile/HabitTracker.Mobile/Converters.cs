using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace HabitTracker.Mobile;

public sealed class BoolOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? 1d : .38d;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class BoolVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is true) != (parameter?.ToString() == "invert") ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class BoolBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var colors = (parameter?.ToString() ?? "#D2F580|#F0F3EA").Split('|');
        return new SolidColorBrush(Parse(value is true ? colors[0] : colors.Length > 1 ? colors[1] : "#F0F3EA"));
    }
    private static Color Parse(string value) => Color.FromArgb(255,
        byte.Parse(value.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
        byte.Parse(value.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
        byte.Parse(value.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
