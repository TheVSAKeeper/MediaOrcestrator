using System.Text.Json;

namespace MediaOrcestrator.Runner;

public sealed class CommentsViewSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public string? SelectedSourceId { get; set; }
    public int Limit { get; set; } = 1000;
    public string Search { get; set; } = "";
    public int FetchSinceDays { get; set; }
    public int FetchOnlyRecent { get; set; }

    public static CommentsViewSettings Load()
    {
        var path = GetPath();

        if (!File.Exists(path))
        {
            return new();
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<CommentsViewSettings>(json, JsonOptions) ?? new();
        }
        catch
        {
            return new();
        }
    }

    public void Save()
    {
        var path = GetPath();

        try
        {
            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(path, json);
        }
        catch
        {
        }
    }

    private static string GetPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "comments-view-settings.json");
    }
}
