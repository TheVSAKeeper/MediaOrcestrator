using System.Text.Json.Serialization;

namespace MediaOrcestrator.Rutube;

[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(UploadSessionRequest))]
[JsonSerializable(typeof(UploadSessionResponse))]
[JsonSerializable(typeof(MetadataUpdateRequest))]
[JsonSerializable(typeof(VideoDetailsResponse))]
[JsonSerializable(typeof(ThumbnailResponse))]
[JsonSerializable(typeof(GetVideoApiResponse))]
[JsonSerializable(typeof(GetVideoApiItem))]
[JsonSerializable(typeof(PublicationRequest))]
[JsonSerializable(typeof(PublicationResponse))]
[JsonSerializable(typeof(List<CategoryInfo>))]
internal sealed partial class RutubeJsonContext : JsonSerializerContext
{
}
