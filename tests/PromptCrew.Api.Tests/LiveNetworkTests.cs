using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PromptCrew.Client;
using PromptCrew.Core;

namespace PromptCrew.Api.Tests;

public sealed class LiveNetworkTests
{
    [Fact]
    public async Task ActualHttpRecoversAfterServerRestartAndLostWriteReplyWithoutDuplicates()
    {
        await using var server = new LiveServer(); await server.Start();
        using var api = new CrewClient(server.Address, server.ReadKey);
        var p = await api.SaveProject(null, new() { Name = "真实网络验收" });
        var t = RoleTemplates.All[0];
        var e = await api.SaveEmployee(p.Id, null, new() { Name = t.Name, Role = t.Role, Responsibilities = t.Responsibilities, BasePrompt = t.BasePrompt });
        var c = await api.SaveChain(p.Id, null, new() { Name = "重试", Goal = "唯一记录" });
        using var bad = new CrewClient(server.Address, () => new string('x', 40));
        Assert.Equal(401, (await Assert.ThrowsAsync<CrewApiException>(() => bad.Session())).Status);
        var other = await api.SaveProject(null, new() { Name = "受限项目" });
        var key = await api.IssueKey(new() { Name = "仅一个项目", ProjectIds = [p.Id] });
        using var scoped = new CrewClient(server.Address, () => key.Token);
        Assert.Equal(403, (await Assert.ThrowsAsync<CrewApiException>(() => scoped.Chains(other.Id))).Status);

        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var proxy = builder.Build();
        using var upstream = new HttpClient { BaseAddress = new Uri(server.Address) };
        var dropWrite = true;
        proxy.Run(async context =>
        {
            if (context.Request.Path == "/api/session") { await Task.Delay(10000, context.RequestAborted); return; }
            using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), context.Request.Path + context.Request.QueryString);
            request.Headers.Add("X-Api-Key", server.ReadKey());
            if (HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsPut(context.Request.Method))
            {
                request.Content = new StreamContent(context.Request.Body);
                request.Content.Headers.ContentType = new("application/json");
            }
            using var response = await upstream.SendAsync(request, context.RequestAborted);
            var json = await response.Content.ReadAsStringAsync(context.RequestAborted);
            if (dropWrite && context.Request.Method == "POST" && response.IsSuccessStatusCode) { dropWrite = false; context.Abort(); return; }
            context.Response.StatusCode = (int)response.StatusCode; context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(json, context.RequestAborted);
        });
        await proxy.StartAsync();
        var proxyUrl = proxy.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var slow = new CrewClient(proxyUrl, server.ReadKey, TimeSpan.FromMilliseconds(150));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => slow.Session());
        using var cancelling = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => slow.Session(cancelling.Token));
        // A request body can commit even when its response never reaches the caller.
        using var throughProxy = new CrewClient(proxyUrl, server.ReadKey);
        var body = new RoundInput { ClientRequestId = Guid.NewGuid(), EmployeeId = e.Id, Input = "网络中断后仍然只保存一轮" };
        await Assert.ThrowsAsync<HttpRequestException>(() => throughProxy.SaveRound(p.Id, c.Id, body));
        var confirmed = await api.ByRequest(p.Id, body.ClientRequestId);
        var replay = await throughProxy.SaveRound(p.Id, c.Id, body);
        Assert.True(replay.Replayed); Assert.Equal(confirmed.Id, replay.Round.Id);
        Assert.Equal(409, (await Assert.ThrowsAsync<CrewApiException>(() => api.SaveRound(p.Id, c.Id, body with { Input = "不同内容" }))).Status);
        await server.Stop();
        await Assert.ThrowsAsync<HttpRequestException>(() => api.Session());
        await server.Start();
        Assert.Equal(confirmed.Id, (await api.ByRequest(p.Id, body.ClientRequestId)).Id);
        Assert.True((await api.SaveRound(p.Id, c.Id, body)).Replayed);
        Assert.Single((await api.Search(p.Id, new())).Items);
        using var openApi = new HttpClient();
        var schema = await openApi.GetStringAsync(server.Address + "swagger/v1/swagger.json");
        Assert.Contains("activity-page", schema); Assert.Contains("createId", schema);
        await proxy.StopAsync();
    }

    private sealed class LiveServer : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "promptcrew-network-" + Guid.NewGuid().ToString("N"));
        private Process? _process;
        public string Address { get; }
        public LiveServer()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            Address = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";
        }
        public string ReadKey() => CrewClient.ReadKeyFile(Path.Combine(_directory, "admin.key"));
        public async Task Start()
        {
            Directory.CreateDirectory(_directory);
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "PromptCrew.slnx"))) root = root.Parent;
            if (root == null) throw new InvalidOperationException("Repository root missing.");
            var dll = Path.Combine(root.FullName, "src", "PromptCrew.Api", "bin", "Release", "net10.0", "PromptCrew.Api.dll");
            // Match the test configuration in both local runs and CI.
            if (AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Debug" + Path.DirectorySeparatorChar)) dll = dll.Replace(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, Path.DirectorySeparatorChar + "Debug" + Path.DirectorySeparatorChar);
            var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            info.ArgumentList.Add(dll);
            info.Environment["PromptCrew__DataDirectory"] = _directory;
            info.Environment["PromptCrew__AdminKeyFile"] = Path.Combine(_directory, "admin.key");
            info.Environment.Remove("PromptCrew__AdminKey"); info.Environment["PromptCrew__DemoMode"] = "false";
            info.Environment["ASPNETCORE_URLS"] = Address.TrimEnd('/'); info.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            _process = Process.Start(info)!; _process.OutputDataReceived += (_, _) => { }; _process.ErrorDataReceived += (_, _) => { };
            _process.BeginOutputReadLine(); _process.BeginErrorReadLine();
            using var probe = new HttpClient { Timeout = TimeSpan.FromMilliseconds(300) };
            for (var i = 0; i < 120; i++)
            {
                if (_process.HasExited) throw new InvalidOperationException("Live API exited.");
                try { using var health = await probe.GetAsync(Address + "health"); if (health.IsSuccessStatusCode) return; }
                catch (HttpRequestException) { } catch (TaskCanceledException) { }
                await Task.Delay(50);
            }
            throw new TimeoutException("Live API did not start.");
        }
        public async Task Stop()
        {
            if (_process == null) return;
            if (!_process.HasExited) _process.Kill(true);
            await _process.WaitForExitAsync(); _process.Dispose(); _process = null;
        }
        public async ValueTask DisposeAsync()
        {
            await Stop(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            var full = Path.GetFullPath(_directory);
            if (!full.StartsWith(Path.Combine(Path.GetTempPath(), "promptcrew-network-"), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected cleanup path.");
            if (Directory.Exists(full)) Directory.Delete(full, true);
        }
    }
}
