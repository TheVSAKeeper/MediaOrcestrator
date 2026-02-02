using LiteDB;
using MediaOrcestrator.Modules;

namespace MediaOrcestrator.HardDiskDrive;

// todo разделить ответственность между ISourceDefinition and ISourceManager (а у SourceManager внутри будет Definition св-во)
public class HardDiskDriveChannel : ISourceType
{
    public SyncDirection ChannelType => SyncDirection.OnlyUpload;

    public string Name => "HardDiskDrive";

    public IEnumerable<SourceSettings> SettingsKeys { get; } =
    [
        new()
        {
            Key = "path",
            IsRequired = true,
            Title = "путь к папке хранения",
        },
    ];

    public async IAsyncEnumerable<MediaDto> GetMedia(Dictionary<string, string> settings)
    {
        var basePath = settings["path"];
        var dbPath = Path.Combine(basePath, "data.db");
        using var db = new LiteDatabase(dbPath);

        var files = db.GetCollection<DriveMedia>("files").FindAll().ToList();

        foreach (var file in files)
        {
            yield return new MediaDto
            {
                Id = file.Id,
                Description = file.Description,
                Title = file.Title,
            };
        }
    }

    public MediaDto GetMediaById()
    {
        throw new NotImplementedException();
    }

    public MediaDto Download(Dictionary<string, string> settings)
    {
        Console.WriteLine("я загрузил брат");
        throw new NotImplementedException();
    }

    public void Upload(MediaDto media, Dictionary<string, string> settings)
    {
        var basePath = settings["path"];
        var path = Path.Combine(basePath, media.Id);
        if (!Directory.Exists(media.Id))
        {
            Directory.CreateDirectory(path);
        }

        var mainName = "main.mp4";
        var mainFilePath = Path.Combine(path, mainName);

        // todo идеалогически move не верный, но пока безразлично
        File.Move(media.TempDataPath, mainFilePath);

        // todo дублирование
        var dbPath = Path.Combine(basePath, "data.db");
        using var db = new LiteDatabase(dbPath);
        db.GetCollection<DriveMedia>("files").Insert(new DriveMedia
        {
            Id = media.Id,
            Description = media.Description,
            Title = media.Title,
            Path = mainName,
        });
    }
}

public class DriveMedia
{
    public string Id { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public string Path { get; set; }
}
