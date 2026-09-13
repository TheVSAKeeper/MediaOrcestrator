using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Modules;
using MediaOrcestrator.Runner.Wpf.Bootstrap;
using MediaOrcestrator.Runner.Wpf.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Windows;
using System.Windows.Input;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public enum SourcesRelationsViewMode
{
    None = 0,
    List = 1,
    Graph = 2,
}

public sealed partial class SourcesRelationsViewModel : ObservableObject, IPageHeader, IPageRefresh
{
    public string PageTitle => "Источники и связи";

    public string? PageDescription =>
        "Площадки и локальные папки, между которыми переносятся медиа. Связь задаёт направление переноса.";

    public ICommand RefreshCommand => ReloadCommand;

    public string? RefreshTooltip => "Перезагрузить источники и связи";


    private const string ViewModeSettingKey = "wpf.sources.viewMode";

    private readonly Orcestrator _orcestrator;
    private readonly ActionHolder _actionHolder;
    private readonly IServiceProvider _services;
    private readonly StateManager _stateManager;
    private readonly ISettingsStore _settings;
    private readonly IDialogService _dialogs;
    private readonly ILogger<SourcesRelationsViewModel> _logger;
    private bool _loaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListMode))]
    [NotifyPropertyChangedFor(nameof(IsGraphMode))]
    private SourcesRelationsViewMode _viewMode = SourcesRelationsViewMode.List;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _statusMessage;

    public SourcesRelationsViewModel(
        Orcestrator orcestrator,
        ActionHolder actionHolder,
        IServiceProvider services,
        StateManager stateManager,
        ISettingsStore settings,
        IDialogService dialogs,
        ILogger<SourcesRelationsViewModel> logger)
    {
        _orcestrator = orcestrator;
        _actionHolder = actionHolder;
        _services = services;
        _stateManager = stateManager;
        _settings = settings;
        _dialogs = dialogs;
        _logger = logger;

        Sources = [];
        Relations = [];
        PluginTypes = [];

        var savedMode = _settings.GetStringValue(ViewModeSettingKey);

        if (Enum.TryParse<SourcesRelationsViewMode>(savedMode, out var parsed))
        {
            _viewMode = parsed;
        }
    }

    public RangeObservableCollection<SourceCardViewModel> Sources { get; }

    public RangeObservableCollection<RelationCardViewModel> Relations { get; }

    public RangeObservableCollection<ISourceType> PluginTypes { get; }

    public int SourceCount => Sources.Count;

    public int DisabledSourceCount => Sources.Count(s => s.IsDisabled);

    public int RelationCount => Relations.Count;

    public int DisabledRelationCount => Relations.Count(r => r.IsDisabled);

    public bool HasSources => Sources.Count > 0;

    public bool HasRelations => Relations.Count > 0;

    public bool HasAtLeastTwoSources => Sources.Count >= 2;

    public bool HasPluginTypes => PluginTypes.Count > 0;

    public bool ShowEmptySourcesState => !HasSources;

    public bool ShowEmptyRelationsState => HasSources && !HasRelations;

    public bool IsListMode => ViewMode == SourcesRelationsViewMode.List;

    public bool IsGraphMode => ViewMode == SourcesRelationsViewMode.Graph;

    public async Task EnsureLoadedAsync()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await ReloadAsync();
    }

    [RelayCommand]
    public async Task ReloadAsync()
    {
        IsBusy = true;

        using var cts = new CancellationTokenSource();
        var action = _actionHolder.Register("Загрузка источников и связей", "Чтение БД…", 0, cts, kind: ActionKind.Other);

        try
        {
            await Task.Run(LoadFromDomainAsync, cts.Token);
            action.Finish($"Источников: {Sources.Count}, связей: {Relations.Count}");
        }
        catch (OperationCanceledException)
        {
            action.MarkCancelled("Отменено");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось загрузить источники и связи");
            StatusMessage = $"Ошибка загрузки: {ex.Message}";
            action.Fail("Ошибка: " + ex.Message, ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ToggleViewMode()
    {
        ViewMode = ViewMode == SourcesRelationsViewMode.List
            ? SourcesRelationsViewMode.Graph
            : SourcesRelationsViewMode.List;
    }

    [RelayCommand]
    private void SetListMode()
    {
        ViewMode = SourcesRelationsViewMode.List;
    }

    [RelayCommand]
    private void SetGraphMode()
    {
        ViewMode = SourcesRelationsViewMode.Graph;
    }

    [RelayCommand]
    private async Task AddSource()
    {
        var pluginTypes = _orcestrator.GetSourceTypes().Values.ToList();

        if (pluginTypes.Count == 0)
        {
            _dialogs.Info("Источники",
                "Плагины не загружены. Положите DLL плагина в папку плагинов и перезапустите приложение.");
            return;
        }

        var pickerVm = new AddSourceViewModel(pluginTypes);
        var settingsVm = _services.GetRequiredService<SourceSettingsViewModel>();
        settingsVm.AllowBack = true;
        var newSourceId = Guid.NewGuid().ToString();
        var firstShow = true;

        while (true)
        {
            var pickConfirmed = firstShow
                ? await _dialogs.ShowAsync(pickerVm)
                : await _dialogs.ReplaceAsync(pickerVm);
            firstShow = false;

            if (!pickConfirmed || pickerVm.SelectedPlugin is null)
            {
                return;
            }

            var plugin = pickerVm.SelectedPlugin.Source;
            settingsVm.InitializeAdd(plugin, newSourceId, _orcestrator.GetSources());

            var savedConfirmed = await _dialogs.ReplaceAsync(settingsVm);

            if (savedConfirmed && settingsVm.Settings is not null)
            {
                try
                {
                    _orcestrator.AddSource(newSourceId, plugin.Name, settingsVm.Settings);
                    _logger.LogInformation("Добавлен источник {Title} ({Type})", settingsVm.Name, plugin.Name);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Не удалось добавить источник");
                    _dialogs.Error("Источники", $"Не удалось добавить источник: {ex.Message}");
                    return;
                }

                await ReloadAsync();
                return;
            }

            if (!settingsVm.WasBackRequested)
            {
                return;
            }
        }
    }

    [RelayCommand]
    private async Task AddRelation()
    {
        if (!HasAtLeastTwoSources)
        {
            _dialogs.Info("Связи", "Нужно минимум два источника, чтобы создать связь.");
            return;
        }

        var dialogVm = new AddRelationViewModel(_orcestrator.GetSources());

        if (!await _dialogs.ShowAsync(dialogVm) || dialogVm.From is null || dialogVm.To is null)
        {
            return;
        }

        try
        {
            _orcestrator.AddRelation(dialogVm.From, dialogVm.To);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось создать связь");
            _dialogs.Error("Связи", $"Не удалось создать связь: {ex.Message}");
            return;
        }

        await ReloadAsync();
    }

    internal async Task EditSource(SourceCardViewModel card)
    {
        var plugin = ResolvePluginFor(card.Source);

        if (plugin is null)
        {
            return;
        }

        var dialogVm = _services.GetRequiredService<SourceSettingsViewModel>();
        dialogVm.InitializeEdit(card.Source, plugin, _orcestrator.GetSources());

        if (!await _dialogs.ShowAsync(dialogVm) || dialogVm.Settings is null)
        {
            return;
        }

        try
        {
            card.Source.Settings = dialogVm.Settings;
            _orcestrator.UpdateSource(card.Source);
            _logger.LogInformation("Обновлён источник {Title}", card.Title);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось сохранить источник");
            _dialogs.Error("Источники", $"Не удалось сохранить источник: {ex.Message}");
            return;
        }

        await ReloadAsync();
    }

    internal async Task DuplicateSource(SourceCardViewModel card)
    {
        var plugin = ResolvePluginFor(card.Source);

        if (plugin is null)
        {
            return;
        }

        var newSourceId = Guid.NewGuid().ToString();
        var dialogVm = _services.GetRequiredService<SourceSettingsViewModel>();
        dialogVm.InitializeDuplicate(card.Source, plugin, newSourceId, _orcestrator.GetSources());

        if (!await _dialogs.ShowAsync(dialogVm) || dialogVm.Settings is null)
        {
            return;
        }

        try
        {
            _orcestrator.AddSource(newSourceId, plugin.Name, dialogVm.Settings);
            _logger.LogInformation("Создан дубликат источника {Title}", dialogVm.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось создать дубликат");
            _dialogs.Error("Источники", $"Не удалось создать дубликат: {ex.Message}");
            return;
        }

        await ReloadAsync();
    }

    internal void ToggleSourceDisable(SourceCardViewModel card)
    {
        try
        {
            card.Source.IsDisable = !card.Source.IsDisable;
            _orcestrator.UpdateSource(card.Source);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось переключить состояние источника");
            _dialogs.Error("Источники", $"Не удалось переключить состояние: {ex.Message}");
            return;
        }

        _ = ReloadAsync();
    }

    internal void DeleteSource(SourceCardViewModel card)
    {
        try
        {
            _orcestrator.RemoveSource(card.Source.Id);
            _logger.LogInformation("Удалён источник {Title}", card.Title);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось удалить источник");
            _dialogs.Error("Источники", $"Не удалось удалить источник: {ex.Message}");
            return;
        }

        _ = ReloadAsync();
    }

    internal void InvertRelation(RelationCardViewModel card)
    {
        var relation = card.Relation;

        if (relation.From is null || relation.To is null)
        {
            return;
        }

        try
        {
            _orcestrator.InvertRelation(relation.From.Id, relation.To.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось инвертировать связь");
            _dialogs.Error("Связи", $"Не удалось инвертировать связь: {ex.Message}");
            return;
        }

        _ = ReloadAsync();
    }

    internal void DeleteRelation(RelationCardViewModel card)
    {
        var relation = card.Relation;

        if (relation.From is null || relation.To is null)
        {
            return;
        }

        try
        {
            _orcestrator.RemoveRelation(relation.From, relation.To);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось удалить связь");
            _dialogs.Error("Связи", $"Не удалось удалить связь: {ex.Message}");
            return;
        }

        _ = ReloadAsync();
    }

    partial void OnViewModeChanged(SourcesRelationsViewMode value)
    {
        _settings.SetValue(ViewModeSettingKey, value.ToString());
    }

    private ISourceType? ResolvePluginFor(Source source)
    {
        var types = _orcestrator.GetSourceTypes();

        if (source.Type is not null)
        {
            return source.Type;
        }

        var byTypeId = types.Values.FirstOrDefault(t => string.Equals(t.Name, source.TypeId, StringComparison.Ordinal));

        if (byTypeId is null)
        {
            _dialogs.Warning("Источники",
                $"Плагин для типа «{source.TypeId}» не найден. Положите DLL в папку плагинов и перезапустите приложение.");
        }

        return byTypeId;
    }

    private async Task LoadFromDomainAsync()
    {
        var sources = _orcestrator.GetSources();
        var relations = _orcestrator.GetRelations();
        var medias = _orcestrator.GetMedias();
        var pluginTypes = _orcestrator.GetSourceTypes().Values
            .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var sourceStats = BuildSourceStats(sources, medias);
        var relationStats = BuildRelationStats(relations, medias);

        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null)
        {
            return;
        }

        await dispatcher.InvokeAsync(() => Apply(sources, relations, pluginTypes, sourceStats, relationStats));
    }

    private void Apply(
        List<Source> sources,
        List<SourceSyncRelation> relations,
        List<ISourceType> pluginTypes,
        Dictionary<string, SourceCardStats> sourceStats,
        Dictionary<(string From, string To), RelationCardStats> relationStats)
    {
        Sources.ReplaceAll(sources
            .OrderBy(s => s.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(source =>
            {
                var stats = sourceStats.GetValueOrDefault(source.Id, SourceCardStats.Empty);
                return new SourceCardViewModel(source, this, stats);
            }));

        Relations.ReplaceAll(relations.Select(relation =>
        {
            var stats = relationStats.GetValueOrDefault((relation.FromId, relation.ToId), RelationCardStats.Empty);
            return new RelationCardViewModel(relation, this, stats);
        }));

        PluginTypes.ReplaceAll(pluginTypes);

        OnPropertyChanged(nameof(SourceCount));
        OnPropertyChanged(nameof(DisabledSourceCount));
        OnPropertyChanged(nameof(RelationCount));
        OnPropertyChanged(nameof(DisabledRelationCount));
        OnPropertyChanged(nameof(HasSources));
        OnPropertyChanged(nameof(HasRelations));
        OnPropertyChanged(nameof(HasAtLeastTwoSources));
        OnPropertyChanged(nameof(HasPluginTypes));
        OnPropertyChanged(nameof(ShowEmptySourcesState));
        OnPropertyChanged(nameof(ShowEmptyRelationsState));
        AddRelationCommand.NotifyCanExecuteChanged();

        StatusMessage = null;
    }

    internal StateManager StateManager => _stateManager;

    private static Dictionary<string, SourceCardStats> BuildSourceStats(List<Source> sources, List<Media> medias)
    {
        var counters = new Dictionary<string, (int Total, int Ok, int PartialOk, int Missing, int Error, int Skipped)>(StringComparer.Ordinal);

        foreach (var source in sources)
        {
            counters[source.Id] = default;
        }

        foreach (var media in medias)
        {
            if (media.Sources is null)
            {
                continue;
            }

            foreach (var link in media.Sources)
            {
                if (!counters.TryGetValue(link.SourceId, out var c))
                {
                    continue;
                }

                c.Total++;

                switch (link.Status)
                {
                    case MediaStatus.Ok: c.Ok++; break;
                    case MediaStatus.PartialOk: c.PartialOk++; break;
                    case MediaStatus.Missing: c.Missing++; break;
                    case MediaStatus.Error: c.Error++; break;
                    case MediaStatus.Skipped: c.Skipped++; break;
                }

                counters[link.SourceId] = c;
            }
        }

        return counters.ToDictionary(
            kvp => kvp.Key,
            kvp => new SourceCardStats
            {
                Total = kvp.Value.Total,
                Ok = kvp.Value.Ok,
                PartialOk = kvp.Value.PartialOk,
                Missing = kvp.Value.Missing,
                Error = kvp.Value.Error,
                Skipped = kvp.Value.Skipped,
            },
            StringComparer.Ordinal);
    }

    private static Dictionary<(string From, string To), RelationCardStats> BuildRelationStats(List<SourceSyncRelation> relations, List<Media> medias)
    {
        var result = new Dictionary<(string From, string To), RelationCardStats>();

        foreach (var relation in relations)
        {
            if (string.IsNullOrEmpty(relation.FromId) || string.IsNullOrEmpty(relation.ToId))
            {
                continue;
            }

            var transferred = 0;
            var pending = 0;
            var failed = 0;

            foreach (var media in medias)
            {
                if (media.Sources is null)
                {
                    continue;
                }

                MediaSourceLink? fromLink = null;
                MediaSourceLink? toLink = null;

                foreach (var link in media.Sources)
                {
                    if (link.SourceId == relation.FromId)
                    {
                        fromLink = link;
                    }
                    else if (link.SourceId == relation.ToId)
                    {
                        toLink = link;
                    }
                }

                if (fromLink is null || fromLink.Status != MediaStatus.Ok)
                {
                    continue;
                }

                if (toLink is null)
                {
                    pending++;
                    continue;
                }

                switch (toLink.Status)
                {
                    case MediaStatus.Ok:
                    case MediaStatus.PartialOk:
                        transferred++;
                        break;
                    case MediaStatus.Error:
                        failed++;
                        break;
                    case MediaStatus.Missing:
                    case MediaStatus.None:
                        pending++;
                        break;
                }
            }

            result[(relation.FromId, relation.ToId)] = new()
            {
                Transferred = transferred,
                Pending = pending,
                Failed = failed,
            };
        }

        return result;
    }
}
