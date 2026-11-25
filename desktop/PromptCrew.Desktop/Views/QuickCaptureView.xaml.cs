using System.Windows.Controls;
using System.Windows.Threading;

namespace PromptCrew.Desktop.Views;

public partial class QuickCaptureView : UserControl
{
    public QuickCaptureView() => InitializeComponent();
    public void FocusPrompt() => Dispatcher.BeginInvoke(() => PromptInput.Focus(), DispatcherPriority.Input);
}
