using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PromptCrew.Desktop;

public static class Motion
{
    public static readonly DependencyProperty ReduceProperty = DependencyProperty.RegisterAttached("Reduce", typeof(bool), typeof(Motion),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));
    public static void SetReduce(DependencyObject target, bool value) => target.SetValue(ReduceProperty, value);
    public static bool GetReduce(DependencyObject target) => (bool)target.GetValue(ReduceProperty);
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(Motion),
        new PropertyMetadata(false, Changed));
    public static void SetEnabled(DependencyObject target, bool value) => target.SetValue(EnabledProperty, value);
    public static bool GetEnabled(DependencyObject target) => (bool)target.GetValue(EnabledProperty);
    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not FrameworkElement element) return;
        if ((bool)args.NewValue) { element.Loaded += Loaded; element.IsVisibleChanged += VisibilityChanged; }
        else { element.Loaded -= Loaded; element.IsVisibleChanged -= VisibilityChanged; }
    }
    private static void Loaded(object sender, RoutedEventArgs e) => Reveal((FrameworkElement)sender);
    private static void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) { if ((bool)e.NewValue) Reveal((FrameworkElement)sender); }
    private static void Reveal(FrameworkElement element)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        if (GetReduce(element) || !SystemParameters.ClientAreaAnimation || !element.IsVisible) return;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
    }
}
