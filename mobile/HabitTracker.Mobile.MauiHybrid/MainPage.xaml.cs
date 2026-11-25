using Microsoft.Maui.Controls.PlatformConfiguration;
using Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific;

namespace HabitTracker.Mobile.MauiHybrid;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();

        SafeAreaEdges = Microsoft.Maui.SafeAreaEdges.All;
    }
    protected override bool OnBackButtonPressed()
    {
        if (!HabitTracker.Mobile.Services.MobilePlatform.CanGoBack) return base.OnBackButtonPressed();
        HabitTracker.Mobile.Services.MobilePlatform.GoBack?.Invoke();
        return true;
    }
}
