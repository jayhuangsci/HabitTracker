using Microsoft.AspNetCore.Components.WebView.Maui;

namespace HabitTracker.Mobile.MauiHybrid;

public class AndroidWebViewHandler : BlazorWebViewHandler
{
    protected override void ConnectHandler(global::Android.Webkit.WebView webView)
    {
        webView.Settings.SetSupportMultipleWindows(false);
        // Keep the WebView text scaling selected by the user.

        base.ConnectHandler(webView);
    }
}
