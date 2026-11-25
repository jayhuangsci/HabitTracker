using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using PromptCrew.Client;
using PromptCrew.Desktop.ViewModels;

namespace PromptCrew.Desktop;

public sealed class ConnectionWindow : Window
{
    private CancellationTokenSource? _request;
    public ConnectionWindow(WorkspaceViewModel workspace)
    {
        Title = "连接设置 · PromptCrew"; Width = 640; Height = 510; MinWidth = 520; MinHeight = 470;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White; FontFamily = new("Microsoft YaHei UI");
        var panel = new StackPanel { Margin = new Thickness(26) };
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = "连接你的工作台", FontSize = 25, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "本地服务可用 HTTP；远端服务须使用 HTTPS。", Foreground = Brushes.DimGray, Margin = new Thickness(0, 10, 0, 22) });
        panel.Children.Add(new TextBlock { Text = "API 根地址", Margin = new Thickness(0, 0, 0, 7) });
        var url = new TextBox { Text = workspace.Settings.ApiUrl }; panel.Children.Add(url);
        panel.Children.Add(new TextBlock { Text = "项目密钥文件 · 仅保存路径，不显示密钥", Margin = new Thickness(0, 16, 0, 7) });
        var key = new TextBox { Text = workspace.Settings.KeyFile }; panel.Children.Add(key);
        var browse = new Button { Content = "选择密钥文件", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 14) };
        panel.Children.Add(browse);
        browse.Click += (_, _) => { var dialog = new OpenFileDialog { Title = "选择 API Key 文件" }; if (dialog.ShowDialog(this) == true) key.Text = dialog.FileName; };
        var timeoutRow = new StackPanel { Orientation = Orientation.Horizontal }; panel.Children.Add(timeoutRow);
        timeoutRow.Children.Add(new TextBlock { Text = "请求超时（秒）", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
        var timeout = new TextBox { Text = workspace.Settings.TimeoutSeconds.ToString(), Width = 80 }; timeoutRow.Children.Add(timeout);
        var row = new WrapPanel { Margin = new Thickness(0, 20, 0, 0) }; panel.Children.Add(row);
        var test = new Button { Content = "测试连接", Margin = new Thickness(0, 0, 10, 0) }; row.Children.Add(test);
        var connect = new Button { Content = "验证并保存", Background = new SolidColorBrush(Color.FromRgb(210, 245, 128)), Margin = new Thickness(0, 0, 10, 0) }; row.Children.Add(connect);
        var cancel = new Button { Content = "取消测试", IsEnabled = false }; row.Children.Add(cancel);
        cancel.Click += (_, _) => _request?.Cancel();
        var status = new TextBlock { Text = workspace.ConnectionState, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 0), Foreground = Brushes.DarkOliveGreen }; panel.Children.Add(status);
        async Task Execute(bool save)
        {
            if (_request != null) return;
            using var request = new CancellationTokenSource(); _request = request;
            test.IsEnabled = connect.IsEnabled = browse.IsEnabled = url.IsEnabled = key.IsEnabled = timeout.IsEnabled = false; cancel.IsEnabled = true;
            status.Text = "正在验证地址、密钥和项目权限…";
            try
            {
                if (!int.TryParse(timeout.Text, out var seconds)) throw new ArgumentException("超时须为 1–120 秒。");
                if (save) await workspace.ChangeConnection(url.Text, key.Text, seconds, request.Token);
                else await workspace.TestConnectionAsync(url.Text, key.Text, seconds, request.Token);
                status.Text = save ? "连接已验证并保存。" : "连接正常，密钥有效。配置尚未保存。";
                if (save) DialogResult = true;
            }
            catch (Exception e) when (e is not OutOfMemoryException) { status.Text = ConnectionFeedback.Describe(e, request.IsCancellationRequested); }
            finally { _request = null; test.IsEnabled = connect.IsEnabled = browse.IsEnabled = url.IsEnabled = key.IsEnabled = timeout.IsEnabled = true; cancel.IsEnabled = false; }
        }
        test.Click += async (_, _) => await Execute(false);
        connect.Click += async (_, _) => await Execute(true);
        Closed += (_, _) => _request?.Cancel();
    }
}
