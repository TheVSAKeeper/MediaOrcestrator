using LiteDB;

namespace MediaOrcestrator.Domain;

public class MyMediaLinkToSource
{
    public string SourceId { get; set; }
    public string Status { get; set; }
    public string Id { get; set; }

    [BsonIgnore]
    public MyMedia Media { get; set; }
}
