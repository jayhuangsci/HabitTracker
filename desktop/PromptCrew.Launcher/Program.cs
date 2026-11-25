using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PromptCrew.Launcher;

internal static class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(nint owner, string text, string caption, uint type);

    [STAThread]
    private static int Main(string[] args)
    {
        Options? options = null;
        try
        {
            options = Options.Parse(args);
            var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(options.DataDirectory.ToUpperInvariant())));
            using var instance = new Mutex(false, @"Local\PromptCrew-" + identity);
            var acquired = false;
            try
            {
                try { acquired = instance.WaitOne(0); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw new InvalidOperationException("这个工作区已经打开，请使用任务栏中的 PromptCrew 窗口。");
                Run(options).GetAwaiter().GetResult();
                return 0;
            }
            finally { if (acquired) instance.ReleaseMutex(); }
        }
        catch (Exception e)
        {
            var message = "无法启动 PromptCrew：\n\n" + e.Message;
            if (options?.CaptureDirectory is { } capture)
            {
                Directory.CreateDirectory(capture);
                File.WriteAllText(Path.Combine(capture, "launch-error.txt"), message);
            }
            else MessageBoxW(0, message, "PromptCrew 启动失败", 0x10);
            return 1;
        }
    }

    private static async Task Run(Options options)
    {
        var apiPath = Path.Combine(options.PackageDirectory, "api", "PromptCrew.Api.exe");
        var desktopPath = Path.Combine(options.PackageDirectory, "desktop", "PromptCrew.Desktop.exe");
        if (!File.Exists(apiPath) || !File.Exists(desktopPath))
            throw new FileNotFoundException("请先解压完整发布包，并保留 exe 旁的 api、desktop 文件夹。");
        Directory.CreateDirectory(options.DataDirectory);
        var port = ChoosePort(options.Port);
        var address = "http://127.0.0.1:" + port;
        var keyPath = Path.Combine(options.DataDirectory, "admin.key");
        var environment = new Dictionary<string, string>
        {
            ["PromptCrew__DataDirectory"] = options.DataDirectory,
            ["PromptCrew__DemoMode"] = options.Demo.ToString(),
            ["PromptCrew__AdminKeyFile"] = keyPath,
            ["ASPNETCORE_URLS"] = address,
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["PROMPTCREW_API_URL"] = address,
            ["PROMPTCREW_KEY_FILE"] = keyPath,
            ["PROMPTCREW_SETTINGS_FILE"] = Path.Combine(options.DataDirectory, "desktop-settings.json")
        };
        var apiStart = StartInfo(apiPath, options.PackageDirectory, environment);
        apiStart.RedirectStandardOutput = apiStart.RedirectStandardError = true;
        apiStart.WindowStyle = ProcessWindowStyle.Hidden;
        var suffix = Environment.ProcessId.ToString();
        var logPath = Path.Combine(options.DataDirectory, "api-" + suffix + ".log");
        var errorPath = Path.Combine(options.DataDirectory, "api-error-" + suffix + ".log");
        await using var log = File.Create(logPath);
        await using var error = File.Create(errorPath);
        using var api = Process.Start(apiStart) ?? throw new InvalidOperationException("无法启动本地服务。");
        var readOutput = api.StandardOutput.BaseStream.CopyToAsync(log);
        var readError = api.StandardError.BaseStream.CopyToAsync(error);
        try
        {
            await Ready(api, address, keyPath, errorPath);
            File.WriteAllText(Path.Combine(options.DataDirectory, "connection.json"),
                JsonSerializer.Serialize(new { apiUrl = address, keyFile = keyPath }));
            var desktopStart = StartInfo(desktopPath, options.PackageDirectory, environment);
            string? image = null;
            if (options.CaptureDirectory is { } capture)
            {
                Directory.CreateDirectory(capture);
                image = Path.Combine(capture, "desktop.png");
                desktopStart.ArgumentList.Add("--capture"); desktopStart.ArgumentList.Add(image);
            }
            using var desktop = Process.Start(desktopStart) ?? throw new InvalidOperationException("无法打开桌面界面。");
            await desktop.WaitForExitAsync();
            if (desktop.ExitCode != 0) throw new InvalidOperationException("桌面程序意外退出。服务日志：" + errorPath);
            if (image != null && !File.Exists(image)) throw new InvalidOperationException("桌面验证没有生成预期截图。");
        }
        finally
        {
            if (!api.HasExited)
            {
                try { api.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
            }
            await api.WaitForExitAsync();
            await Task.WhenAll(readOutput, readError);
        }
        if (options.CaptureDirectory is { } resultDirectory)
            File.WriteAllText(Path.Combine(resultDirectory, "launch-check.json"), JsonSerializer.Serialize(new
            { ok = true, authenticated = true, apiStopped = api.HasExited, demo = options.Demo, port }));
    }

    private static ProcessStartInfo StartInfo(string file, string directory, Dictionary<string, string> environment)
    {
        var start = new ProcessStartInfo(file) { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true };
        foreach (var pair in environment) start.Environment[pair.Key] = pair.Value;
        start.Environment.Remove("PromptCrew__AdminKey");
        return start;
    }

    private static int ChoosePort(int? requested)
    {
        var first = requested ?? 5283;
        var last = requested ?? 5382;
        for (var port = first; port <= last; port++)
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            try { listener.Start(); return port; }
            catch (SocketException) { }
            finally { listener.Stop(); }
        }
        throw new InvalidOperationException("本机端口不可用，请关闭旧窗口后重新启动，或使用 --port 指定其他端口。");
    }

    private static async Task Ready(Process api, string address, string keyPath, string errorPath)
    {
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
        { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(1) };
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(25))
        {
            if (api.HasExited) throw new InvalidOperationException("本地服务启动失败，日志：" + errorPath);
            try
            {
                using var response = await http.GetAsync("/health");
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                if (response.IsSuccessStatusCode && body.RootElement.GetProperty("product").GetString() == "PromptCrew" && File.Exists(keyPath))
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, "/api/session");
                    request.Headers.Add("X-Api-Key", File.ReadAllText(keyPath).Trim());
                    using var session = await http.SendAsync(request);
                    if (session.IsSuccessStatusCode) return;
                }
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or IOException) { }
            await Task.Delay(150);
        }
        throw new InvalidOperationException("服务启动超时，日志：" + errorPath);
    }
}

internal sealed record Options(bool Demo, string PackageDirectory, string DataDirectory, string? CaptureDirectory, int? Port)
{
    public static Options Parse(string[] args)
    {
        var demo = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "").EndsWith("-Demo", StringComparison.OrdinalIgnoreCase);
        var package = AppContext.BaseDirectory;
        string? data = null, capture = null; int? port = null;
        for (var i = 0; i < args.Length; i++)
        {
            string Value() => ++i < args.Length ? args[i] : throw new ArgumentException("参数缺少值：" + args[i - 1]);
            switch (args[i])
            {
                case "--demo": demo = true; break;
                case "--package-directory": package = Value(); break;
                case "--data-directory": data = Value(); break;
                case "--capture-directory": capture = Value(); break;
                case "--port":
                    if (!int.TryParse(Value(), out var parsed) || parsed is < 1024 or > 65535) throw new ArgumentException("端口范围为 1024–65535。");
                    port = parsed; break;
                default: throw new ArgumentException("未知参数：" + args[i]);
            }
        }
        data ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PromptCrew", demo ? "demo" : "personal");
        return new(demo, Path.GetFullPath(package), Path.GetFullPath(data), capture == null ? null : Path.GetFullPath(capture), port);
    }
}
