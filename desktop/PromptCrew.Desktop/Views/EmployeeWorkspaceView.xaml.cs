using System.Windows;
using System.Windows.Controls;

namespace PromptCrew.Desktop.Views;

public partial class EmployeeWorkspaceView : UserControl
{
    public EmployeeWorkspaceView() => InitializeComponent();
    public void ShowHistory() => EmployeeTabs.SelectedIndex = 0;
    public void ShowEditor(int tab = 0) { EmployeeTabs.SelectedIndex = 1; TaskEditor.ShowTab(tab); }
    public void ShowProfile() => EmployeeTabs.SelectedIndex = 2;
    private void GoEditor(object sender, RoutedEventArgs e) => ShowEditor();
}
