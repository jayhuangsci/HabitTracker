using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using HabitTracker.Models;

namespace HabitTracker.Services;

public class ApiService
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public ApiService(string baseUrl)
    {
        var normalizedBaseUrl = baseUrl.EndsWith("/") ? baseUrl : $"{baseUrl}/";

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(normalizedBaseUrl)
        };
    }

    public async Task<List<HabitDto>> GetHabitsAsync()
    {
        var response = await _httpClient.GetAsync("api/habits");
        return await ReadAsync<List<HabitDto>>(response);
    }

    public async Task<HabitDto> CreateHabitAsync(CreateHabitRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync("api/habits", request, _jsonOptions);
        return await ReadAsync<HabitDto>(response);
    }

    public async Task<HabitDto> UpdateHabitAsync(int id, UpdateHabitRequest request)
    {
        var response = await _httpClient.PutAsJsonAsync($"api/habits/{id}", request, _jsonOptions);
        return await ReadAsync<HabitDto>(response);
    }

    public async Task DeleteHabitAsync(int id)
    {
        var response = await _httpClient.DeleteAsync($"api/habits/{id}");
        await EnsureSuccessAsync(response);
    }

    public async Task<List<HabitLogDto>> GetHabitLogsAsync(int? habitId = null)
    {
        var url = habitId.HasValue
            ? $"api/habit-logs?habitId={habitId.Value}"
            : "api/habit-logs";

        var response = await _httpClient.GetAsync(url);
        return await ReadAsync<List<HabitLogDto>>(response);
    }


    public async Task<HabitLogDto> CreateHabitLogAsync(CreateHabitLogRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync("api/habit-logs", request, _jsonOptions);
        return await ReadAsync<HabitLogDto>(response);
    }

    public async Task<HabitLogDto> UpdateHabitLogAsync(int id, UpdateHabitLogRequest request)
    {
        var response = await _httpClient.PutAsJsonAsync($"api/habit-logs/{id}", request, _jsonOptions);
        return await ReadAsync<HabitLogDto>(response);
    }

    public async Task DeleteHabitLogAsync(int id)
    {
        var response = await _httpClient.DeleteAsync($"api/habit-logs/{id}");
        await EnsureSuccessAsync(response);
    }

    public async Task<HabitStatsDto> GetHabitStatsAsync(int habitId, string range)
    {
        var response = await _httpClient.GetAsync($"api/stats/habits/{habitId}?range={Uri.EscapeDataString(range)}");
        return await ReadAsync<HabitStatsDto>(response);
    }

    private async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"API request failed: {(int)response.StatusCode} {error}");
        }

        var result = await response.Content.ReadFromJsonAsync<T>(_jsonOptions);
        return result ?? throw new InvalidOperationException("API returned empty response.");
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"API request failed: {(int)response.StatusCode} {error}");
        }

        await Task.CompletedTask;
    }
}
