using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HabitTracker.Services;
using HabitTracker.ViewModels;

namespace HabitTracker;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        if (Environment.GetCommandLineArgs().Contains("--capture")) Height = 1120;
        var baseUrl = Environment.GetEnvironmentVariable("HABITTRACKER_API_URL") ?? "http://localhost:5253/";
        if (!baseUrl.EndsWith('/')) baseUrl += "/";
        var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(12) };
        var vm = new FitnessViewModel(new FitnessApiClient(http), new HabitListViewModel(new ApiService(baseUrl)));
        DataContext = vm;
        Closed += (_, _) => { vm.Dispose(); http.Dispose(); };
        Loaded += async (_, _) =>
        {
            await vm.InitializeAsync();
            var args = Environment.GetCommandLineArgs();
            var pageIndex = Array.IndexOf(args, "--page");
            if (pageIndex >= 0 && pageIndex + 1 < args.Length) vm.Navigate(args[pageIndex + 1]);
            var captureIndex = Array.IndexOf(args, "--capture");
            if (captureIndex >= 0 && captureIndex + 1 < args.Length)
            {
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                ShellRoot.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)ShellRoot.ActualWidth, (int)ShellRoot.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(ShellRoot);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                var path = Path.GetFullPath(args[captureIndex + 1]); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using (var stream = File.Create(path)) encoder.Save(stream);
                Close();
            }
        };
    }
}
