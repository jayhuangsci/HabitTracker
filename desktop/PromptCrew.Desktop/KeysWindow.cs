using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using PromptCrew.Core;
using PromptCrew.Desktop.ViewModels;

namespace PromptCrew.Desktop;

public sealed class KeysWindow : Window
{
    private readonly WorkspaceViewModel _workspace;
    private readonly StackPanel _projects = new();
    private readonly TextBox _name = new() { Text = "本地 Agent", MaxLength = 100 };
    private readonly TextBox _token = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap };
    private readonly ListBox _keys = new() { Height = 160, DisplayMemberPath = "Name" };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _issue = new() { Content = "签发项目密钥" };
    private readonly Button _revoke = new() { Content = "撤销所选密钥" };
    public KeysWindow(WorkspaceViewModel workspace)
    {
        _workspace = workspace;
        Title = "Agent 访问权限"; Width = 640; Height = 740; MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new("Microsoft YaHei UI"); Background = System.Windows.Media.Brushes.WhiteSmoke;
        var form = new StackPanel { Margin = new Thickness(24) };
        void Label(string text) => form.Children.Add(new TextBlock { Text = text, Margin = new Thickness(0, 16, 0, 8), TextWrapping = TextWrapping.Wrap });
        Label("为 Agent 签发仅能访问所选项目的密钥。密钥只在签发时显示一次。");
        Label("密钥名称"); form.Children.Add(_name);
        Label("允许访问的项目");
        foreach (var project in workspace.Projects)
            _projects.Children.Add(new CheckBox { Content = project.Name, Tag = project.Id, IsChecked = project.Id == workspace.SelectedProject?.Id, Margin = new Thickness(0, 4, 0, 4) });
        form.Children.Add(new ScrollViewer { Content = _projects, MaxHeight = 120 });
        _issue.Margin = new Thickness(0, 16, 0, 8); _issue.Click += async (_, _) => await Run(Issue);
        form.Children.Add(_issue); form.Children.Add(_token);
        var save = new Button { Content = "保存本次密钥到文件", Margin = new Thickness(0, 8, 0, 0) };
        save.Click += (_, _) =>
        {
            if (_token.Text.Length == 0) return;
            var dialog = new SaveFileDialog { Title = "保存 Agent 密钥", FileName = "promptcrew-agent.key", Filter = "Key 文件|*.key" };
            if (dialog.ShowDialog(this) != true) return;
            try { File.WriteAllText(dialog.FileName, _token.Text); _status.Text = "密钥已保存。将文件路径配置给 Agent，妥善保管该文件。"; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _status.Text = "保存失败：" + e.Message; }
        };
        form.Children.Add(save); Label("已签发密钥 · 只显示名称，不显示密钥原文"); form.Children.Add(_keys);
        _revoke.Margin = new Thickness(0, 8, 0, 12); _revoke.Click += async (_, _) => await Run(async () =>
        {
            if (_keys.SelectedItem is not AccessKeyDto key) return;
            await workspace.Api.RevokeKey(key.Id); _status.Text = "密钥已撤销。"; await Refresh();
        });
        form.Children.Add(_revoke); form.Children.Add(_status);
        Content = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Loaded += async (_, _) => await Run(Refresh);
    }
    private async Task Refresh() => _keys.ItemsSource = (await _workspace.Api.Keys()).Where(k => k.RevokedUtc == null).ToArray();
    private async Task Issue()
    {
        var ids = _projects.Children.OfType<CheckBox>().Where(x => x.IsChecked == true).Select(x => (Guid)x.Tag).ToArray();
        var issued = await _workspace.Api.IssueKey(new AccessKeyInput { Name = _name.Text, ProjectIds = ids });
        _token.Text = issued.Token; _status.Text = "已签发。离开此窗口后无法再次查看密钥原文，请先保存。";
        await Refresh();
    }
    private async Task Run(Func<Task> work)
    {
        _issue.IsEnabled = _revoke.IsEnabled = false;
        try { await work(); }
        catch (Exception e) when (e is PromptCrew.Client.CrewApiException or HttpRequestException or TaskCanceledException or IOException) { _status.Text = e.Message; }
        finally { _issue.IsEnabled = _revoke.IsEnabled = true; }
    }
}
