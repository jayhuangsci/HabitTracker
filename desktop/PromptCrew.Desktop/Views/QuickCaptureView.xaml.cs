using System.Windows.Controls;
using System.Windows.Threading;

namespace PromptCrew.Desktop.Views;

public partial class QuickCaptureView : UserControl
{
    public QuickCaptureView() => InitializeComponent();
    public void FocusPrompt() => Dispatcher.BeginInvoke(() =>
    {
        PromptInput.Focus();
        // Focus must not scroll the task selection out of sight when the panel first opens.
        Dispatcher.BeginInvoke(() => ComposerScroll.ScrollToTop(), DispatcherPriority.ApplicationIdle);
    }, DispatcherPriority.Input);
}
