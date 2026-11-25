using System.Text.Json;
using PromptCrew.Client;
using PromptCrew.Core;
using PromptCrew.Tools;

if (args.Length != 2 || args[0] != "invoke")
{
    Console.Error.WriteLine("Usage: PromptCrew.Tools invoke request.json\nConfigure PROMPTCREW_API_URL and PROMPTCREW_KEY_FILE. See docs/promptcrew-agent.md.");
    return 2;
}
ToolRequest? request = null;
var path = Path.GetFullPath(args[1]);
try
{
    if (new FileInfo(path).Length > 2_000_000) throw new ArgumentException("Request file exceeds 2 MB.");
    request = JsonSerializer.Deserialize<ToolRequest>(await File.ReadAllTextAsync(path), CrewJson.Options)
        ?? throw new ArgumentException("Request file is empty.");
    var address = Environment.GetEnvironmentVariable("PROMPTCREW_API_URL") ?? "http://127.0.0.1:5283/";
    var keyFile = Environment.GetEnvironmentVariable("PROMPTCREW_KEY_FILE") ?? throw new ArgumentException("PROMPTCREW_KEY_FILE is required.");
    using var api = new CrewClient(address, () => CrewClient.ReadKeyFile(keyFile));
    var data = await ToolRunner.Execute(api, request);
    Console.WriteLine(JsonSerializer.Serialize(new { ok = true, data }, CrewJson.Options));
    return 0;
}
catch (Exception e) when (e is CrewApiException or HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
{
    Guid? retryId = null;
    if (request?.Body.ValueKind == JsonValueKind.Object && request.Body.TryGetProperty("clientRequestId", out var id) && id.ValueKind == JsonValueKind.String && id.TryGetGuid(out var guid)) retryId = guid;
    var apiError = e as CrewApiException;
    Console.WriteLine(JsonSerializer.Serialize(new { ok = false, error = new { code = apiError?.Code ?? "tool_error", status = apiError?.Status, message = e.Message },
        clientRequestId = retryId, requestFile = path }, CrewJson.Options));
    return 1;
}
