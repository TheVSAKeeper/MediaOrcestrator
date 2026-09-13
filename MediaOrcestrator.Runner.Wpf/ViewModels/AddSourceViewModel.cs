using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaOrcestrator.Modules;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class AddSourceViewModel : ObservableObject, IDialogViewModel
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(DetailsCard))]
    private PluginPickViewModel? _selectedPlugin;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyFilterState))]
    private string _filter = string.Empty;

    [ObservableProperty]
    private int _visibleCount;

    public AddSourceViewModel(IEnumerable<ISourceType> plugins)
    {
        var items = plugins
            .Select(PluginPickViewModel.From)
            .OrderBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        Plugins = new(items);
        TotalCount = items.Count;
        VisibleCount = items.Count;

        PluginsView = CollectionViewSource.GetDefaultView(Plugins);
        PluginsView.Filter = PluginFilter;

        if (PluginsView is ICollectionView collectionView)
        {
            collectionView.CollectionChanged += (_, _) => VisibleCount = Plugins.Count(PluginFilter);
        }
    }

    public event EventHandler<bool>? RequestClose;

    public ObservableCollection<PluginPickViewModel> Plugins { get; }

    public ICollectionView PluginsView { get; }

    public int TotalCount { get; }

    public bool HasPlugins => TotalCount > 0;

    public bool HasSelection => SelectedPlugin is not null;

    public bool ShowEmptyFilterState => TotalCount > 0 && VisibleCount == 0;

    public PluginDetailsViewModel? DetailsCard => SelectedPlugin is null
        ? null
        : new(SelectedPlugin);

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private void Continue()
    {
        RequestClose?.Invoke(this, true);
    }

    [RelayCommand]
    private void Cancel()
    {
        SelectedPlugin = null;
        RequestClose?.Invoke(this, false);
    }

    [RelayCommand]
    private void ClearFilter()
    {
        Filter = string.Empty;
    }

    private bool CanContinue()
    {
        return SelectedPlugin is not null;
    }

    partial void OnFilterChanged(string value)
    {
        PluginsView.Refresh();
        VisibleCount = Plugins.Count(PluginFilter);

        if (SelectedPlugin is not null && !PluginFilter(SelectedPlugin))
        {
            SelectedPlugin = null;
        }
    }

    private bool PluginFilter(object obj)
    {
        if (obj is not PluginPickViewModel plugin)
        {
            return false;
        }

        return PluginFilter(plugin);
    }

    private bool PluginFilter(PluginPickViewModel plugin)
    {
        if (string.IsNullOrWhiteSpace(Filter))
        {
            return true;
        }

        var needle = Filter.Trim();

        return plugin.DisplayName.Contains(needle, StringComparison.CurrentCultureIgnoreCase)
            || plugin.TypeId.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || plugin.CategoryLabel.Contains(needle, StringComparison.CurrentCultureIgnoreCase)
            || (plugin.Description?.Contains(needle, StringComparison.CurrentCultureIgnoreCase) ?? false);
    }
}

public sealed class PluginPickViewModel
{
    private PluginPickViewModel(ISourceType source, PluginPresentation presentation)
    {
        Source = source;
        DisplayName = presentation.DisplayName;
        IconKind = presentation.IconKind;
        CategoryLabel = presentation.CategoryLabel;
        Description = presentation.Description;
        AccentBrushKey = presentation.AccentBrushKey;
        DirectionLabel = DirectionToLabel(source.ChannelType);
        DirectionIconKind = DirectionToIconKind(source.ChannelType);
        RequiresAuth = source is IAuthenticatable;
        RequiredFieldsCount = source.SettingsKeys?.Count(s => s.IsRequired) ?? 0;
        TotalFieldsCount = source.SettingsKeys?.Count() ?? 0;
    }

    public ISourceType Source { get; }

    public string TypeId => Source.Name;

    public string DisplayName { get; }

    public SourcePluginIconKind IconKind { get; }

    public string CategoryLabel { get; }

    public string? Description { get; }

    public string AccentBrushKey { get; }

    public string DirectionLabel { get; }

    public SourceDirectionIconKind DirectionIconKind { get; }

    public bool RequiresAuth { get; }

    public int RequiredFieldsCount { get; }

    public int TotalFieldsCount { get; }

    public string FieldsSummary => RequiredFieldsCount > 0
        ? $"{RequiredFieldsCount} обязат. из {TotalFieldsCount}"
        : TotalFieldsCount > 0
            ? $"{TotalFieldsCount} настроек"
            : "Без настроек";

    public static PluginPickViewModel From(ISourceType source)
    {
        return new(source, PluginPresentationCatalog.For(source.Name));
    }

    private static string DirectionToLabel(SyncDirection direction)
    {
        return direction switch
        {
            SyncDirection.OnlyDownload => "Только импорт",
            SyncDirection.OnlyUpload => "Только публикация",
            SyncDirection.Full => "Импорт и публикация",
            _ => "Направление не указано",
        };
    }

    private static SourceDirectionIconKind DirectionToIconKind(SyncDirection direction)
    {
        return direction switch
        {
            SyncDirection.OnlyDownload => SourceDirectionIconKind.Download,
            SyncDirection.OnlyUpload => SourceDirectionIconKind.Upload,
            SyncDirection.Full => SourceDirectionIconKind.Bidirectional,
            _ => SourceDirectionIconKind.Unknown,
        };
    }
}

public sealed class PluginDetailsViewModel(PluginPickViewModel plugin)
{
    public PluginPickViewModel Plugin { get; } = plugin;

    public string Title => Plugin.DisplayName;

    public string TypeId => Plugin.TypeId;

    public SourcePluginIconKind IconKind => Plugin.IconKind;

    public string CategoryLabel => Plugin.CategoryLabel;

    public string? Description => Plugin.Description;

    public string AccentBrushKey => Plugin.AccentBrushKey;

    public string DirectionLabel => Plugin.DirectionLabel;

    public SourceDirectionIconKind DirectionIconKind => Plugin.DirectionIconKind;

    public bool RequiresAuth => Plugin.RequiresAuth;

    public string AuthLabel => Plugin.RequiresAuth
        ? "Потребуется авторизация"
        : "Авторизация не требуется";

    public string FieldsSummary => Plugin.FieldsSummary;
}

internal readonly record struct PluginPresentation(
    string DisplayName,
    SourcePluginIconKind IconKind,
    string CategoryLabel,
    string? Description,
    string AccentBrushKey);

public enum SourcePluginIconKind
{
    None = 0,
    YouTube = 1,
    Rutube = 2,
    VkVideo = 3,
    Telegram = 4,
    HardDiskDrive = 5,
    Plugin = 6,
}

public enum SourceDirectionIconKind
{
    None = 0,
    Download = 1,
    Upload = 2,
    Bidirectional = 3,
    Unknown = 4,
}

internal static class PluginPresentationCatalog
{
    private static readonly Dictionary<string, PluginPresentation> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["youtube"] = new(
            "YouTube",
            SourcePluginIconKind.YouTube,
            "Видеохостинг",
            "Канал YouTube. Импорт и публикация видео, нужен доступ к аккаунту через браузер.",
            "Accent.Primary"),
        ["rutube"] = new(
            "RuTube",
            SourcePluginIconKind.Rutube,
            "Видеохостинг",
            "Канал RuTube. Импорт и публикация роликов, авторизация через сохранённую сессию.",
            "Accent.Primary"),
        ["vk_video"] = new(
            "VK Видео",
            SourcePluginIconKind.VkVideo,
            "Видеохостинг",
            "Сообщество или профиль ВКонтакте. Импорт и публикация видео, в т. ч. короткие.",
            "Accent.Primary"),
        ["VkVideo"] = new(
            "VK Видео",
            SourcePluginIconKind.VkVideo,
            "Видеохостинг",
            "Сообщество или профиль ВКонтакте. Импорт и публикация видео, в т. ч. короткие.",
            "Accent.Primary"),
        ["telegram"] = new(
            "Telegram",
            SourcePluginIconKind.Telegram,
            "Мессенджер",
            "Канал или чат в Telegram. Импорт видеосообщений и публикация видео.",
            "Accent.Primary"),
        ["hard_disk_drive"] = new(
            "Локальный диск",
            SourcePluginIconKind.HardDiskDrive,
            "Локальное хранилище",
            "Папка на компьютере. Импорт уже имеющихся файлов и сохранение копий.",
            "Accent.Primary"),
        ["HardDiskDrive"] = new(
            "Локальный диск",
            SourcePluginIconKind.HardDiskDrive,
            "Локальное хранилище",
            "Папка на компьютере. Импорт уже имеющихся файлов и сохранение копий.",
            "Accent.Primary"),
    };

    public static PluginPresentation For(string typeId)
    {
        if (Known.TryGetValue(typeId, out var hit))
        {
            return hit;
        }

        return new(
            Prettify(typeId),
            SourcePluginIconKind.Plugin,
            "Плагин",
            $"Источник «{typeId}» из внешнего плагина. Параметры – в карточке.",
            "Accent.Primary");
    }

    private static string Prettify(string typeId)
    {
        if (string.IsNullOrWhiteSpace(typeId))
        {
            return "Источник";
        }

        var parts = typeId
            .Replace('_', ' ')
            .Replace('-', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => char.ToUpper(p[0]) + p[1..]);

        return string.Join(' ', parts);
    }
}
