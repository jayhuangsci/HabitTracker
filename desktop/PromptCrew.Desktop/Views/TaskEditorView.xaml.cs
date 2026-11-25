using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PromptCrew.Core;
using PromptCrew.Desktop.ViewModels;

namespace PromptCrew.Desktop.Views;

public partial class TaskEditorView : UserControl
{
    public TaskEditorView() => InitializeComponent();
    private WorkspaceViewModel Workspace => (WorkspaceViewModel)DataContext;
    public void ShowTab(int tab) => EditorTabs.SelectedIndex = Math.Clamp(tab, 0, 4);
    private async void RoundSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    { if (e.NewValue is RoundNode node && !Workspace.IsBusy) await Workspace.SelectRoundAsync(node.Id); }
    private void GoCompose(object sender, RoutedEventArgs e) => ShowTab(0);
    private void GoRecordContext(object sender, RoutedEventArgs e) => ShowTab(2);
    private async void EditorTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is WorkspaceViewModel workspace && ReferenceEquals(e.Source, EditorTabs) && EditorTabs.SelectedIndex == 3 && !workspace.IsBusy)
            await workspace.PrepareComparisonAsync();
    }
    private void ClearSearch(object sender, RoutedEventArgs e)
    { Workspace.SearchEmployee = null; Workspace.SearchText = ""; Workspace.SearchTag = ""; Workspace.SearchState = Workspace.SearchStates[0]; }
    private async void OpenSearchResult(object sender, MouseButtonEventArgs e)
    { if (sender is ListBox { SelectedItem: RoundDto row }) await Workspace.OpenRoundAsync(row); }
}
