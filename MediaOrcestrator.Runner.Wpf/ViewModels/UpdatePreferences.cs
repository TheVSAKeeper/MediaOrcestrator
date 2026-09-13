using CommunityToolkit.Mvvm.ComponentModel;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed class UpdatePreferences : ObservableObject
{
    public const string RepoKey = "update_repo";

    private const string LastResortRepo = "TheVSAKeeper/MediaOrcestrator";

    private readonly ISettingsStore _settings;

    private readonly bool _loaded;

    private string _repo;

    public UpdatePreferences(ISettingsStore settings)
    {
        _settings = settings;
        _repo = Normalize(settings.GetStringValue(RepoKey));
        _loaded = true;
    }

    public static string FactoryRepo { get; } = string.IsNullOrWhiteSpace(BuildConstants.DefaultUpdateRepo)
        ? LastResortRepo
        : BuildConstants.DefaultUpdateRepo;

    public string Repo
    {
        get => _repo;
        set
        {
            var normalized = Normalize(value);

            if (!SetProperty(ref _repo, normalized))
            {
                return;
            }

            if (_loaded)
            {
                _settings.SetValue(RepoKey, normalized);
            }
        }
    }

    public bool IsFactoryRepo => string.Equals(Repo, FactoryRepo, StringComparison.OrdinalIgnoreCase);

    public void ResetRepo()
    {
        Repo = FactoryRepo;
    }

    private static string Normalize(string? raw)
    {
        var trimmed = raw?.Trim();

        return string.IsNullOrEmpty(trimmed) ? FactoryRepo : trimmed;
    }
}
