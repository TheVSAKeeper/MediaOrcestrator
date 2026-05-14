using System.Text.Json;
using System.Text.Json.Serialization;

namespace MediaOrcestrator.Runner;

public enum CommentsLayoutMode
{
    Grouped = 0,
    Flat = 1,
}

public enum CommentsSortKey
{
    Newest = 0,
    Oldest = 1,
    MostLikes = 2,
}

public sealed class CommentsViewSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string? SelectedSourceId { get; set; }
    public int Limit { get; set; } = 1000;
    public string Search { get; set; } = "";
    public int FetchSinceDays { get; set; }
    public int FetchOnlyRecent { get; set; }
    public CommentsLayoutMode LayoutMode { get; set; } = CommentsLayoutMode.Grouped;
    public CommentsSortKey SortKey { get; set; } = CommentsSortKey.Newest;

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
