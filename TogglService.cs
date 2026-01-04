using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace FocusHudWpf;

public class TogglService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiToken;

    public TogglService(string apiToken)
    {
        _apiToken = apiToken;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://api.track.toggl.com/api/v9/")
        };

        var authValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_apiToken}:api_token"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authValue);
    }

    // Replace with your actual workspace ID logic or fetch it dynamically
    // For now, we will let Toggl auto-detect workspace for simplistic calls if possible,
    // but usually workspace_id is required. We'll start simple.
    
    public async Task<TogglTimeEntry?> GetCurrentTimeEntryAsync()
    {
        try 
        {
            var response = await _httpClient.GetAsync("me/time_entries/current");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<TogglTimeEntry>(json);
            }
        }
        catch (Exception ex)
        {
            // Fail silently or log
            System.Diagnostics.Debug.WriteLine($"Toggl Error: {ex.Message}");
        }
        return null;
    }

    public async Task<List<TogglProject>> GetProjectsAsync(int workspaceId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"workspaces/{workspaceId}/projects?active=true");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<List<TogglProject>>(json) ?? new List<TogglProject>();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Toggl Error: {ex.Message}");
        }
        return new List<TogglProject>();
    }

    public async Task StartTimeEntryAsync(string description, int workspaceId, int? projectId = null)
    {
        var entry = new
        {
            description = description,
            tags = new string[] { "FocusHUD" },
            workspace_id = workspaceId,
            project_id = projectId,
            created_with = "FocusHUD",
            start = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            duration = -1
        };

        var json = JsonSerializer.Serialize(entry);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        await _httpClient.PostAsync($"workspaces/{workspaceId}/time_entries", content);
    }

    public async Task StopTimeEntryAsync(long timeEntryId, int workspaceId)
    {
        await _httpClient.PatchAsync($"workspaces/{workspaceId}/time_entries/{timeEntryId}/stop", null);
    }
    public async Task<int?> GetDefaultWorkspaceIdAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("me");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var user = JsonSerializer.Deserialize<TogglUser>(json);
                return user?.DefaultWorkspaceId;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Toggl Error: {ex.Message}");
        }
        return null;
    }
}

// Data Models
public class TogglUser
{
    [JsonPropertyName("default_workspace_id")]
    public int DefaultWorkspaceId { get; set; }
}

public class TogglTimeEntry
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("workspace_id")]
    public int WorkspaceId { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("start")]
    public DateTime Start { get; set; }
    
    [JsonPropertyName("duration")]
    public long Duration { get; set; } // If negative, it's running. duration = -(start_time_unix)
}

public class TogglProject
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
    
    [JsonPropertyName("color")]
    public string? Color { get; set; }
}
