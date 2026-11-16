using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HabitTracker.Core;

namespace HabitTracker.Services;

public sealed class FitnessApiClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    public async Task<IReadOnlyList<WorkoutResponse>> ListAsync() =>
        await http.GetFromJsonAsync<WorkoutResponse[]>("api/workouts", Json) ?? [];
    public async Task<bool> IsDemoAsync()
    {
        using var doc = JsonDocument.Parse(await http.GetStringAsync("health"));
        return doc.RootElement.GetProperty("demo").GetBoolean();
    }
    public async Task<WorkoutResponse> CreateAsync(CreateWorkoutRequest request) =>
        await ReadAsync(await http.PostAsJsonAsync("api/workouts", request, Json));
    public async Task<WorkoutResponse> AddSetAsync(int id, LogSetRequest request) =>
        await ReadAsync(await http.PostAsJsonAsync($"api/workouts/{id}/sets", request, Json));
    public async Task<WorkoutResponse> DeleteSetAsync(int id, int setId) =>
        await ReadAsync(await http.DeleteAsync($"api/workouts/{id}/sets/{setId}"));
    public async Task<WorkoutResponse> CompleteAsync(int id) =>
        await ReadAsync(await http.PostAsync($"api/workouts/{id}/complete", null));

    private static async Task<WorkoutResponse> ReadAsync(HttpResponseMessage response)
    {
        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var message = await response.Content.ReadAsStringAsync();
                try
                {
                    using var doc = JsonDocument.Parse(message);
                    if (doc.RootElement.TryGetProperty("detail", out var detail)) message = detail.GetString() ?? message;
                    else if (doc.RootElement.TryGetProperty("title", out var title)) message = title.GetString() ?? message;
                }
                catch (JsonException) { message = "The journal service could not complete the request."; }
                throw new InvalidOperationException(message);
            }
            return await response.Content.ReadFromJsonAsync<WorkoutResponse>(Json) ?? throw new InvalidOperationException("The journal returned an empty response.");
        }
    }
}
