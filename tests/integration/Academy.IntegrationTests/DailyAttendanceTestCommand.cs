using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Academy.IntegrationTests;

internal static class DailyAttendanceTestCommand
{
    // Ordinary scenarios read the same source version as the UI. Stale-write
    // scenarios explicitly bypass this helper and assert the conflict instead.
    public static async Task<object> WithCurrentVersion(HttpClient client, string path, object body)
    {
        const string root = "/api/v1/manage/staff-attendance/";
        if (!path.StartsWith(root) || !Guid.TryParse(path[root.Length..], out _)) return body;
        var data = JsonSerializer.SerializeToNode(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
        var response = await client.GetAsync($"{path}?date={data["date"]}");
        if (!response.IsSuccessStatusCode) return body;
        var current = await response.Content.ReadFromJsonAsync<JsonElement>();
        data["version"] = current.GetProperty("version").GetUInt32();
        return data;
    }
}
