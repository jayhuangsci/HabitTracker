using System.Text.Json;
using PromptCrew.Client;
using PromptCrew.Core;
using PromptCrew.Tools;

if (args.Length != 2 || args[0] != "invoke")
{
    Console.Error.WriteLine("Usage: PromptCrew.Tools invoke request.json\nConfigure PROMPTCREW_API_URL and PROMPTCREW_KEY_FILE. See docs/promptcrew-agent.md.");
    return 2;
}
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
ToolRequest? request = null;
var path = Path.GetFullPath(args[1]);
try
{
    if (new FileInfo(path).Length > 2_000_000) throw new ArgumentException("Request file exceeds 2 MB.");
    request = JsonSerializer.Deserialize<ToolRequest>(await File.ReadAllTextAsync(path), CrewJson.Options)
        ?? throw new ArgumentException("Request file is empty.");
    var address = Environment.GetEnvironmentVariable("PROMPTCREW_API_URL") ?? "http://127.0.0.1:5283/";
    var keyFile = Environment.GetEnvironmentVariable("PROMPTCREW_KEY_FILE") ?? throw new ArgumentException("PROMPTCREW_KEY_FILE is required.");
    var timeoutText = Environment.GetEnvironmentVariable("PROMPTCREW_TIMEOUT_SECONDS") ?? "20";
    if (!int.TryParse(timeoutText, out var timeoutSeconds) || timeoutSeconds is < 1 or > 120) throw new ArgumentException("PROMPTCREW_TIMEOUT_SECONDS must be 1–120.");
    using var api = new CrewClient(address, () => CrewClient.ReadKeyFile(keyFile), TimeSpan.FromSeconds(timeoutSeconds));
    var data = await ToolRunner.Execute(api, request, cancellation.Token);
    Console.WriteLine(JsonSerializer.Serialize(new { ok = true, data }, CrewJson.Options));
    return 0;
}
catch (Exception e) when (e is CrewApiException or HttpRequestException or OperationCanceledException or IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
{
    Guid? retryId = null;
    if (request?.Body.ValueKind == JsonValueKind.Object && request.Body.TryGetProperty("clientRequestId", out var id) && id.ValueKind == JsonValueKind.String && id.TryGetGuid(out var guid)) retryId = guid;
    var apiError = e as CrewApiException;
    Console.WriteLine(JsonSerializer.Serialize(new { ok = false, error = new { code = apiError?.Code ?? (e is OperationCanceledException ? cancellation.IsCancellationRequested ? "request_cancelled" : "network_timeout" : "tool_error"), status = apiError?.Status, message = ConnectionFeedback.Describe(e, cancellation.IsCancellationRequested) },
        clientRequestId = retryId, requestFile = path }, CrewJson.Options));
    return 1;
}
