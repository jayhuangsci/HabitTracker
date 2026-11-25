using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PromptCrew.Desktop.ViewModels;

namespace PromptCrew.Desktop;

public sealed class ConnectionWindow : Window
{
    public ConnectionWindow(WorkspaceViewModel workspace)
    {
        Title = "连接设置 · PromptCrew"; Width = 610; Height = 360; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = System.Windows.Media.Brushes.White; FontFamily = new("Microsoft YaHei UI");
        var panel = new StackPanel { Margin = new Thickness(25) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = "连接你的提示词工作台", FontSize = 23, Margin = new Thickness(0, 0, 0, 20) });
        panel.Children.Add(new TextBlock { Text = "API 根地址", Margin = new Thickness(0, 0, 0, 7) });
        var url = new TextBox { Text = workspace.Settings.ApiUrl }; panel.Children.Add(url);
        panel.Children.Add(new TextBlock { Text = "API Key 文件 · 文件内容不会在界面显示", Margin = new Thickness(0, 14, 0, 7) });
        var key = new TextBox { Text = workspace.Settings.KeyFile, ToolTip = "启动脚本会配置管理员密钥文件，也可选择受限项目密钥文件。" }; panel.Children.Add(key);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) }; panel.Children.Add(row);
        var browse = new Button { Content = "选择密钥文件", Margin = new Thickness(0, 0, 10, 0) }; row.Children.Add(browse);
        browse.Click += (_, _) => { var dialog = new OpenFileDialog { Title = "选择 API Key 文件" }; if (dialog.ShowDialog(this) == true) key.Text = dialog.FileName; };
        var connect = new Button { Content = "验证并连接", Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(210, 245, 128)) }; row.Children.Add(connect);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), Foreground = System.Windows.Media.Brushes.DarkGoldenrod }; panel.Children.Add(status);
        connect.Click += async (_, _) =>
        {
            connect.IsEnabled = false;
            try { await workspace.ChangeConnection(url.Text, key.Text); DialogResult = true; }
            catch (Exception e) { status.Text = e.Message; }
            finally { connect.IsEnabled = true; }
        };
    }
}
