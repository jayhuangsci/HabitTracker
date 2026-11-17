using System.Windows;
using HabitTracker.Mobile.Core;
using HabitTracker.Mobile.Services;

namespace HabitTracker.Mobile;

public sealed partial class App : Application
{
    public App()
    {
        InitializeComponent();
        var device = MobilePlatform.Device ?? new BrowserDevice();
        var clock = TimeProvider.System;
        var workspace = new MobileWorkspace(new JournalFactory(device.Store, clock), device.Store, clock, device.ExportCsvAsync);
        RootVisual = new MainPage(workspace, device);
    }
}
