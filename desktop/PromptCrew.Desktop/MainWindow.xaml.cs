using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PromptCrew.Core;
using PromptCrew.Desktop.ViewModels;

namespace PromptCrew.Desktop;

public partial class MainWindow : Window
{
    private readonly WorkspaceViewModel _workspace;
    public MainWindow(WorkspaceViewModel workspace)
    {
        InitializeComponent(); _workspace = workspace; DataContext = workspace;
        workspace.EditRequested += (section, edit) => new CatalogWindow(new CatalogEditor(workspace, section, edit)) { Owner = this }.ShowDialog();
        workspace.ConnectionRequested += () => new ConnectionWindow(workspace) { Owner = this }.ShowDialog();
        workspace.KeysRequested += () => new KeysWindow(workspace) { Owner = this }.ShowDialog();
        Closing += (_, _) => workspace.Dispose();
        Loaded += async (_, _) =>
        {
            await workspace.InitializeAsync();
            var args = Environment.GetCommandLineArgs();
            var tab = Array.IndexOf(args, "--tab");
            if (tab >= 0 && tab + 1 < args.Length && int.TryParse(args[tab + 1], out var index)) EditorTabs.SelectedIndex = Math.Clamp(index, 0, 4);
            var capture = Array.IndexOf(args, "--capture");
            if (capture >= 0 && capture + 1 < args.Length)
            {
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Capture(args[capture + 1]); Close();
            }
        };
    }
    private async void RoundSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is RoundNode node && !_workspace.IsBusy) await _workspace.SelectRoundAsync(node.Id);
    }
    private void GoCompose(object sender, RoutedEventArgs e) => EditorTabs.SelectedIndex = 0;
    private async void EditorTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.Source, EditorTabs) && EditorTabs.SelectedIndex == 3 && !_workspace.IsBusy) await _workspace.PrepareComparisonAsync();
    }
    private void ClearSearch(object sender, RoutedEventArgs e)
    { _workspace.SearchEmployee = null; _workspace.SearchText = ""; _workspace.SearchTag = ""; _workspace.SearchState = _workspace.SearchStates[0]; }
    private async void OpenSearchResult(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox { SelectedItem: RoundDto row })
        { await _workspace.OpenRoundAsync(row); EditorTabs.SelectedIndex = 1; }
    }
    public void Capture(string path)
    {
        UpdateLayout();
        var target = new RenderTargetBitmap(Math.Max(1, (int)ActualWidth), Math.Max(1, (int)ActualHeight), 96, 96, PixelFormats.Pbgra32);
        target.Render(this);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(target));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
