using CommunityToolkit.Mvvm.ComponentModel;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class UpdatePreferences : ObservableObject
{
    public const string RepoKey = "update_repo";
    public const string CheckOnStartupKey = "wpf.update.check";

    private const string LastResortRepo = "TheVSAKeeper/MediaOrcestrator";

    private readonly ISettingsStore _settings;

    private readonly bool _loaded;

    private string _repo;

    [ObservableProperty]
    private bool _checkOnStartup = true;

    public UpdatePreferences(ISettingsStore settings)
    {
        _settings = settings;
        _repo = Normalize(settings.GetStringValue(RepoKey));
        _checkOnStartup = settings.GetBool(CheckOnStartupKey, fallback: true);
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

    partial void OnCheckOnStartupChanged(bool value)
    {
        if (_loaded)
        {
            _settings.SetBool(CheckOnStartupKey, value);
        }
    }

    private static string Normalize(string? raw)
    {
        var trimmed = raw?.Trim();

        return string.IsNullOrEmpty(trimmed) ? FactoryRepo : trimmed;
    }
}
