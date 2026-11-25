using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PromptCrew.Desktop.ViewModels;

namespace PromptCrew.Desktop;

public partial class MainWindow : Window
{
    private readonly WorkspaceViewModel _workspace;
    private Guid? _overviewProject;
    public MainWindow(WorkspaceViewModel workspace)
    {
        InitializeComponent(); _workspace = workspace; DataContext = workspace;
        workspace.EditRequested += (section, edit) => new CatalogWindow(new CatalogEditor(workspace, section, edit)) { Owner = this }.ShowDialog();
        workspace.ConnectionRequested += () => new ConnectionWindow(workspace) { Owner = this }.ShowDialog();
        workspace.KeysRequested += () => new KeysWindow(workspace) { Owner = this }.ShowDialog();
        workspace.LeavingOverview += OverviewPanel.SavePosition;
        workspace.OverviewShown += OverviewPanel.RestorePosition;
        workspace.EmployeeOpened += EmployeePanel.ShowHistory;
        workspace.RecordOpened += () => EmployeePanel.ShowEditor(1);
        workspace.PropertyChanged += ProjectChanged;
        Closing += (_, _) =>
        {
            workspace.PropertyChanged -= ProjectChanged;
            workspace.Dispose();
        };
        Loaded += async (_, _) =>
        {
            await workspace.InitializeAsync();
            var args = Environment.GetCommandLineArgs();
            string? Argument(string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
            var view = Argument("--view") ?? "overview";
            var hasTab = int.TryParse(Argument("--tab"), out var tab);
            if (view != "overview" || hasTab)
            {
                var employee = Guid.TryParse(Argument("--employee-id"), out var id) ? workspace.Employees.FirstOrDefault(x => x.Id == id)
                    : workspace.Employees.FirstOrDefault(x => x.Role.Contains("程序")) ?? workspace.Employees.FirstOrDefault();
                if (employee != null)
                {
                    await workspace.OpenEmployeeAsync(employee.Id);
                    if (view == "profile") EmployeePanel.ShowProfile();
                    else if (view == "record")
                    {
                        var card = workspace.ActiveTimeline?.Cards.LastOrDefault();
                        if (card != null) await workspace.OpenActivityAsync(card.Id);
                        if (hasTab) EmployeePanel.ShowEditor(tab);
                    }
                    else if (hasTab) EmployeePanel.ShowEditor(tab);
                }
            }
            var capture = Argument("--capture");
            if (capture != null)
            {
                for (var i = 0; workspace.IsBusy && i < 1200; i++) await Task.Delay(25);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                if (workspace.HasError)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(capture))!);
                    File.WriteAllText(capture + ".error.txt", workspace.Status); Environment.ExitCode = 1;
                }
                else Capture(capture);
                Close();
            }
        };
    }
    private void ProjectChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceViewModel.OverviewTitle) && _overviewProject != _workspace.SelectedProject?.Id)
        { _overviewProject = _workspace.SelectedProject?.Id; OverviewPanel.ResetPosition(); }
    }
    private void OpenSettings(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = "连接设置", Command = _workspace.ConnectionCommand });
        menu.Items.Add(new MenuItem { Header = "API Key 与项目权限", Command = _workspace.KeysCommand });
        menu.PlacementTarget = (UIElement)sender; menu.IsOpen = true;
    }
    public void Capture(string path)
    {
        UpdateLayout();
        var content = (FrameworkElement)Content;
        var target = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(content.ActualWidth)), Math.Max(1, (int)Math.Ceiling(content.ActualHeight)), 96, 96, PixelFormats.Pbgra32);
        target.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(target));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
