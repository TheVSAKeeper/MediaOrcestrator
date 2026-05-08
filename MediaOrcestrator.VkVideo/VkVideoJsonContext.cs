using System.Text.Json.Serialization;

namespace MediaOrcestrator.VkVideo;

[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(List<WebTokenResponse>))]
[JsonSerializable(typeof(VkCommentsResponse))]
[JsonSerializable(typeof(VideoGetByIdsResponse))]
[JsonSerializable(typeof(VideoGetResponse))]
[JsonSerializable(typeof(VideoSaveResponse))]
[JsonSerializable(typeof(FileUploadResponse))]
[JsonSerializable(typeof(PublishResponse))]
[JsonSerializable(typeof(EditResponse))]
[JsonSerializable(typeof(SaveThumbResponse))]
[JsonSerializable(typeof(ShortVideoThumbUploadUrlResponse))]
[JsonSerializable(typeof(ShortVideoEncodeProgressResponse))]
[JsonSerializable(typeof(VideoForEditResponse))]
[JsonSerializable(typeof(VkLikesAddResponse))]
[JsonSerializable(typeof(GroupsGetByIdResponse))]
[JsonSerializable(typeof(CatalogResponse))]
[JsonSerializable(typeof(CatalogSectionResponse))]
[JsonSerializable(typeof(long))]
internal sealed partial class VkVideoJsonContext : JsonSerializerContext
{
}
