using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using HabitTracker.Core;

namespace HabitTracker.Mobile.Core;

/// <summary>A connected journal accesses the existing API directly. It never silently writes to the local journal.</summary>
public sealed class ApiWorkoutJournal(HttpClient http) : IWorkoutJournal
{
    public static Uri NormalizeAddress(string address)
    {
        if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new ArgumentException("Enter an http(s) API address without credentials, query parameters, or a fragment.");
        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
    }

    public async Task PingAsync(CancellationToken ct = default)
    {
        using var response = await http.GetAsync("health", ct);
        await EnsureSuccessAsync(response, ct);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (!document.RootElement.TryGetProperty("status", out var status) || status.GetString() != "healthy")
            throw new InvalidOperationException("The journal service is not ready.");
    }

    public async Task<IReadOnlyList<WorkoutResponse>> ListAsync(CancellationToken ct = default) =>
        await GetAsync("api/workouts", JournalJsonContext.Default.WorkoutResponseArray, ct);
    public Task<WorkoutResponse> CreateAsync(CreateWorkoutRequest request, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "api/workouts", request, JournalJsonContext.Default.CreateWorkoutRequest, JournalJsonContext.Default.WorkoutResponse, ct);
    public Task<WorkoutResponse> AddSetAsync(int id, LogSetRequest request, CancellationToken ct = default)
    {
        request.Validate();
        return SendAsync(HttpMethod.Post, $"api/workouts/{id}/sets", request, JournalJsonContext.Default.LogSetRequest, JournalJsonContext.Default.WorkoutResponse, ct);
    }
    public async Task<WorkoutResponse> RemoveSetAsync(int id, int setId, CancellationToken ct = default)
    {
        using var response = await http.DeleteAsync($"api/workouts/{id}/sets/{setId}", ct);
        return await ReadAsync(response, JournalJsonContext.Default.WorkoutResponse, ct);
    }
    public async Task<WorkoutResponse> CompleteAsync(int id, CancellationToken ct = default)
    {
        using var response = await http.PostAsync($"api/workouts/{id}/complete", null, ct);
        return await ReadAsync(response, JournalJsonContext.Default.WorkoutResponse, ct);
    }
    public async Task<IReadOnlyList<HabitRecord>> ListHabitsAsync(CancellationToken ct = default) =>
        await GetAsync("api/habits", JournalJsonContext.Default.HabitRecordArray, ct);
    public Task<HabitRecord> SaveHabitAsync(int? id, SaveHabitRequest request, CancellationToken ct = default) =>
        SendAsync(id.HasValue ? HttpMethod.Put : HttpMethod.Post, id.HasValue ? $"api/habits/{id}" : "api/habits",
            request, JournalJsonContext.Default.SaveHabitRequest, JournalJsonContext.Default.HabitRecord, ct);
    public async Task DeleteHabitAsync(int id, CancellationToken ct = default)
    {
        using var response = await http.DeleteAsync($"api/habits/{id}", ct);
        await EnsureSuccessAsync(response, ct);
    }
    public async Task<IReadOnlyList<HabitLogRecord>> ListHabitLogsAsync(int id, CancellationToken ct = default) =>
        await GetAsync($"api/habit-logs?habitId={id}", JournalJsonContext.Default.HabitLogRecordArray, ct);
    public async Task<HabitLogRecord> SaveHabitLogAsync(SaveHabitLogRequest request, CancellationToken ct = default)
    {
        var existing = (await ListHabitLogsAsync(request.HabitId, ct)).FirstOrDefault(l => l.Date == request.Date);
        return await SendAsync(existing == null ? HttpMethod.Post : HttpMethod.Put, existing == null ? "api/habit-logs" : $"api/habit-logs/{existing.Id}",
            request, JournalJsonContext.Default.SaveHabitLogRequest, JournalJsonContext.Default.HabitLogRecord, ct);
    }
    public async Task DeleteHabitLogAsync(int id, CancellationToken ct = default)
    {
        using var response = await http.DeleteAsync($"api/habit-logs/{id}", ct);
        await EnsureSuccessAsync(response, ct);
    }

    private async Task<T> GetAsync<T>(string route, JsonTypeInfo<T> type, CancellationToken ct)
    {
        using var response = await http.GetAsync(route, ct);
        return await ReadAsync(response, type, ct);
    }
    private async Task<TResponse> SendAsync<TRequest, TResponse>(HttpMethod method, string route, TRequest request,
        JsonTypeInfo<TRequest> requestType, JsonTypeInfo<TResponse> responseType, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(method, route) { Content = JsonContent.Create(request, requestType) };
        using var response = await http.SendAsync(message, ct);
        return await ReadAsync(response, responseType, ct);
    }
    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, JsonTypeInfo<T> type, CancellationToken ct)
    {
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync(type, ct) ?? throw new InvalidOperationException("The journal returned an empty response.");
    }
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var message = "The journal service could not complete this request.";
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            foreach (var name in new[] { "detail", "message", "title" })
                if (document.RootElement.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String)
                { message = field.GetString() ?? message; break; }
        }
        catch (JsonException) { }
        if (response.StatusCode == HttpStatusCode.Conflict) throw new JournalConflictException(message);
        if (response.StatusCode == HttpStatusCode.NotFound) throw new JournalNotFoundException(message);
        throw new InvalidOperationException(message);
    }
}
