using System.Windows;
using PromptCrew.Client;
using PromptCrew.Desktop.ViewModels;

namespace PromptCrew.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var settings = DesktopSettings.Load();
        settings.ApiUrl = Environment.GetEnvironmentVariable("PROMPTCREW_API_URL") ?? settings.ApiUrl;
        settings.KeyFile = Environment.GetEnvironmentVariable("PROMPTCREW_KEY_FILE") ?? settings.KeyFile;
        try { _ = CrewClient.Address(settings.ApiUrl); }
        catch (ArgumentException) { settings.ApiUrl = "http://127.0.0.1:5283/"; settings.Warning = "已恢复默认服务地址，请检查连接设置。"; }
        var vm = new WorkspaceViewModel(settings);
        MainWindow = new MainWindow(vm); MainWindow.Show();
    }
}
