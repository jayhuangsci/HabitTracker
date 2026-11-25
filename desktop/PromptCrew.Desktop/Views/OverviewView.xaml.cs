using System.Windows.Controls;
using System.Windows.Threading;

namespace PromptCrew.Desktop.Views;

public partial class OverviewView : UserControl
{
    private double _horizontal, _vertical;
    public OverviewView() => InitializeComponent();
    public void SavePosition() { _horizontal = HistoryScroll.HorizontalOffset; _vertical = HistoryScroll.VerticalOffset; }
    public void RestorePosition() => Dispatcher.BeginInvoke(() =>
    { HistoryScroll.ScrollToHorizontalOffset(_horizontal); HistoryScroll.ScrollToVerticalOffset(_vertical); }, DispatcherPriority.Loaded);
    public void ResetPosition() { _horizontal = _vertical = 0; RestorePosition(); }
}
