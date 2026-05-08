using System.Text.Json.Serialization;

namespace MediaOrcestrator.Youtube;

[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.Never, PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(YtDlpInfoJson))]
[JsonSerializable(typeof(PlaywrightAuthState))]
internal sealed partial class YoutubeJsonContext : JsonSerializerContext
{
}

internal sealed class YtDlpInfoJson
{
    public string? Id { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Uploader { get; set; }
    public string? Channel { get; set; }
    public string? Thumbnail { get; set; }
    public double? Duration { get; set; }
    public long? ViewCount { get; set; }
    public long? Timestamp { get; set; }
    public string? UploadDate { get; set; }
    public List<string>? Tags { get; set; }
}

internal sealed class PlaywrightAuthState
{
    public List<PlaywrightCookie>? Cookies { get; set; }
}

internal sealed class PlaywrightCookie
{
    public string? Name { get; set; }
    public string? Value { get; set; }
    public string? Domain { get; set; }
    public string? Path { get; set; }
    public bool Secure { get; set; }
    public double Expires { get; set; }
}
