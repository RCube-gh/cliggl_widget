using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace FocusHudWpf;

public class ClickUpService
{
    private readonly HttpClient _httpClient;

    public ClickUpService(string apiToken)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://api.clickup.com/api/v2/")
        };
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(apiToken);
    }

    public async Task<List<ClickUpTask>> GetTasksAsync(string listId)
    {
        var tasks = new List<ClickUpTask>();
        try
        {
            // Fetch top 15 active tasks, ordered by updated time (or priority if preferred)
            var response = await _httpClient.GetAsync($"list/{listId}/task?archived=false&page=0&order_by=updated&reverse=true&include_closed=false");
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<ClickUpTasksResponse>(json);
                
                if (result?.Tasks != null)
                {
                    tasks.AddRange(result.Tasks);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ClickUp Error: {ex.Message}");
        }
        return tasks;
    }
}

// Data Models
public class ClickUpTasksResponse
{
    [JsonPropertyName("tasks")]
    public ClickUpTask[]? Tasks { get; set; }
}

public class ClickUpTask
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
