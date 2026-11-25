using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using HabitTracker.Mobile.Services;
using OpenSilver.MauiHybrid.Runner;

namespace HabitTracker.Mobile.MauiHybrid;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            })
            .ConfigureMauiHandlers(conf =>
            {
#if ANDROID
                conf.AddHandler<BlazorWebView, AndroidWebViewHandler>();
#endif
            });

        builder.ConfigureLifecycleEvents(events =>
        {
#if ANDROID
            events.AddAndroid(android => android.OnResume(_ => MobilePlatform.Resuming?.Invoke()).OnStop(_ => MobilePlatform.Suspending?.Invoke()));
#elif IOS
            events.AddiOS(ios => ios.OnActivated(_ => MobilePlatform.Resuming?.Invoke()).DidEnterBackground(_ => MobilePlatform.Suspending?.Invoke()));
#endif
        });
        builder.Services.AddScoped<IMauiHybridRunner, MauiHybridRunner>();
        builder.Services.AddMauiBlazorWebView();
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
