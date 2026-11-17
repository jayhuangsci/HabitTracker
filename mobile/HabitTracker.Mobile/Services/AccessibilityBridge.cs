using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using OpenSilver;

namespace HabitTracker.Mobile.Services;

/// <summary>Add explicit browser semantics to templated OpenSilver controls, including dynamically expanded cards.</summary>
public static class AccessibilityBridge
{
    private static readonly DependencyProperty LastStateProperty = DependencyProperty.RegisterAttached("LastState", typeof(string), typeof(AccessibilityBridge), new PropertyMetadata(null));

    public static void Update(DependencyObject root)
    {
        if (root is FrameworkElement element) Annotate(element);
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++) Update(VisualTreeHelper.GetChild(root, i));
    }

    private static void Annotate(FrameworkElement element)
    {
        var role = element switch { CheckBox => "checkbox", ComboBox => "combobox", DatePicker => "textbox", TextBox => "textbox", ComboBoxItem => "option", Button => "button", _ => null };
        if (role == null) return;
        var name = AutomationProperties.GetName(element);
        if (string.IsNullOrEmpty(name) && element is Button { Content: string text }) name = text;
        var enabled = element is not Control control || control.IsEnabled;
        var state = element switch
        {
            CheckBox check => check.IsChecked == true ? "true" : check.IsChecked == false ? "false" : "mixed",
            ComboBox combo => combo.IsDropDownOpen ? "true" : "false",
            ComboBoxItem item => item.IsSelected ? "true" : "false",
            _ => ""
        };
        var key = role + "|" + name + "|" + enabled + "|" + state;
        if ((string?)element.GetValue(LastStateProperty) == key) return;
        try
        {
            var div = Interop.GetDiv(element);
            var applied = Interop.ExecuteJavaScript("window.formDevice.annotate($0,$1,$2,$3,$4)", div, role, name ?? "", enabled, state);
            if (Convert.ToBoolean(applied)) element.SetValue(LastStateProperty, key);
        }
        catch (InvalidOperationException) { /* Retry after a collapsed or newly templated control enters the DOM. */ }
    }
}
