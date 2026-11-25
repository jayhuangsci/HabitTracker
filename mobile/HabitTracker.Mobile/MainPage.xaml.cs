using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using HabitTracker.Mobile.Core;
using HabitTracker.Mobile.Services;
using OpenSilver;

namespace HabitTracker.Mobile;

public partial class MainPage : Page
{
    private readonly MobileWorkspace _workspace;
    private readonly IMobileDevice _device;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private IDisposable? _browserLifecycle;
    private bool _loaded;
    private bool _interopReady, _annotating;

    public MainPage(MobileWorkspace workspace, IMobileDevice device)
    {
        InitializeComponent();
        _workspace = workspace; _device = device;
        DataContext = workspace;
        workspace.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName != nameof(MobileWorkspace.Page)) return;
            MobilePlatform.CanGoBack = workspace.Page != MobilePage.Home;
            Dispatcher.BeginInvoke(() => { ContentScroll.ScrollToVerticalOffset(0); WorkoutContent.ConfigureInputModes(); });
        };
        MobilePlatform.GoBack = () => Dispatcher.BeginInvoke(() => workspace.Navigate(MobilePage.Home));
        Loaded += OnLoaded;
        LayoutUpdated += (_, _) =>
        {
            if (!_interopReady || _annotating) return;
            _annotating = true;
            try { AccessibilityBridge.Update(this); }
            finally { _annotating = false; }
        };
        _timer.Tick += async (_, _) => await _workspace.TickAsync();
        MobilePlatform.Resuming = () => Dispatcher.BeginInvoke(async () => await _workspace.TickAsync());
        MobilePlatform.Suspending = () => Dispatcher.BeginInvoke(async () =>
        {
            try { await _workspace.FlushAsync(); }
            catch (IOException) { /* Records are committed at each action; a suspended draft can be retried on resume. */ }
        });
    }
    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            await Interop.LoadJavaScriptFile("ms-appx:///HabitTracker.Mobile/Assets/form-device.js");
            _interopReady = true;
            await _workspace.InitializeAsync(_device.LaunchDemo);
            if (_device is BrowserDevice)
            {
                _browserLifecycle = Interop.CreateJavascriptCallback((Action<string>)(kind =>
                {
                    if (kind == "resume") MobilePlatform.Resuming?.Invoke(); else MobilePlatform.Suspending?.Invoke();
                }));
                Interop.ExecuteJavaScript("window.formDevice.lifecycle($0)", _browserLifecycle);
                var view = Interop.ExecuteJavaScript("window.formDevice.initialView()")?.ToString();
                if (Enum.TryParse<MobilePage>(view, true, out var page)) _workspace.Navigate(page);
            }
            Interop.ExecuteJavaScript("window.formDevice.ready()");
            WorkoutContent.ConfigureInputModes();
            AccessibilityBridge.Update(this);
            _timer.Start();
        }
        catch (Exception ex)
        {
            BootFailure.Text = "The journal could not open. " + ex.Message;
            BootFailure.Visibility = Visibility.Visible;
        }
    }
    public static void SetInputMode(TextBox box, string mode)
    {
        try { Interop.ExecuteJavaScript("window.formDevice && window.formDevice.inputMode($0, $1)", Interop.GetDiv(box), mode); }
        catch (InvalidOperationException) { /* A collapsed field gets its input mode when the workout page is shown. */ }
    }
}
