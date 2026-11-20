using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Modules;
using MediaOrcestrator.Runner.Wpf.Bootstrap;
using MediaOrcestrator.Runner.Wpf.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Windows.Input;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class SourcesRelationsViewModel : ObservableObject, IPageHeader, IPageRefresh, IPageEscapeHandler
{
    public string PageTitle => "Источники и связи";

    public string? PageDescription =>
        "Площадки и локальные папки, между которыми переносятся медиа. Связь задаёт направление переноса.";

    public ICommand RefreshCommand => IsAuthenticationMode ? RefreshAuthenticationCommand : ReloadCommand;

    public string? RefreshTooltip => IsAuthenticationMode ? "Проверить авторизацию" : "Перезагрузить источники и связи";

    private const string ViewModeSettingKey = "wpf.sources.viewMode";

    private readonly Orcestrator _orcestrator;
    private readonly ActionHolder _actionHolder;
    private readonly IServiceProvider _services;
    private readonly StateManager _stateManager;
    private readonly ISettingsStore _settings;
    private readonly IDialogService _dialogs;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger<SourcesRelationsViewModel> _logger;
    private bool _loaded;
    private List<SourceCardViewModel>? _automationSourcesStash;
    private List<RelationCardViewModel>? _automationRelationsStash;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListMode))]
    [NotifyPropertyChangedFor(nameof(IsGraphMode))]
    [NotifyPropertyChangedFor(nameof(IsAuthenticationMode))]
    [NotifyPropertyChangedFor(nameof(RefreshCommand))]
    [NotifyPropertyChangedFor(nameof(RefreshTooltip))]
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
        IUiDispatcher dispatcher,
        SourcesAuthenticationViewModel authentication,
        ILogger<SourcesRelationsViewModel> logger)
    {
        _orcestrator = orcestrator;
        _actionHolder = actionHolder;
        _services = services;
        _stateManager = stateManager;
        _settings = settings;
        _dialogs = dialogs;
        _dispatcher = dispatcher;
        _logger = logger;
        Authentication = authentication;
        Authentication.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(Authentication.CanCloseDetails))
            {
                OnPropertyChanged(nameof(CanHandleEscape));
            }
        };

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
    public SourcesAuthenticationViewModel Authentication { get; }

    public bool IsAuthenticationMode
    {
        get => ViewMode == SourcesRelationsViewMode.Authentication;
        set
        {
            if (value)
            {
                ViewMode = SourcesRelationsViewMode.Authentication;
            }
        }
    }

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

    public bool IsListMode
    {
        get => ViewMode == SourcesRelationsViewMode.List;
        set
        {
            if (value)
            {
                ViewMode = SourcesRelationsViewMode.List;
            }
        }
    }

    public bool IsGraphMode
    {
        get => ViewMode == SourcesRelationsViewMode.Graph;
        set
        {
            if (value)
            {
                ViewMode = SourcesRelationsViewMode.Graph;
            }
        }
    }

    internal StateManager StateManager => _stateManager;

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
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
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
    private void SetListMode() => ViewMode = SourcesRelationsViewMode.List;

    [RelayCommand]
    private void SetGraphMode() => ViewMode = SourcesRelationsViewMode.Graph;

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
                await TryMutateAsync("Источники", "Не удалось добавить источник", () =>
                {
                    _orcestrator.AddSource(newSourceId, plugin.Name, settingsVm.Settings);
                    _logger.LogInformation("Добавлен источник {Title} ({Type})", settingsVm.Name, plugin.Name);
                });
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

        await TryMutateAsync("Связи", "Не удалось создать связь",
            () => _orcestrator.AddRelation(dialogVm.From, dialogVm.To));
    }

    internal async Task DuplicateSource(SourceCardViewModel card)
    {
        if (ResolvePluginFor(card.Source) is not { } plugin)
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

        await TryMutateAsync("Источники", "Не удалось создать дубликат", () =>
        {
            _orcestrator.AddSource(newSourceId, plugin.Name, dialogVm.Settings);
            _logger.LogInformation("Создан дубликат источника {Title}", dialogVm.Name);
        });
    }

    internal void ToggleSourceDisable(SourceCardViewModel card)
    {
        TryMutateAndReload("Источники", "Не удалось переключить состояние", () =>
        {
            card.Source.IsDisable = !card.Source.IsDisable;
            _orcestrator.UpdateSource(card.Source);
        });
    }

    internal void DeleteSource(SourceCardViewModel card)
    {
        TryMutateAndReload("Источники", "Не удалось удалить источник", () =>
        {
            _orcestrator.RemoveSource(card.Source.Id);
            _logger.LogInformation("Удалён источник {Title}", card.Title);
        });
    }

    internal void InvertRelation(RelationCardViewModel card)
    {
        var relation = card.Relation;

        if (relation.From is null || relation.To is null)
        {
            return;
        }

        TryMutateAndReload("Связи", "Не удалось инвертировать связь",
            () => _orcestrator.InvertRelation(relation.From.Id, relation.To.Id));
    }

    internal void DeleteRelation(RelationCardViewModel card)
    {
        var relation = card.Relation;

        TryMutateAndReload("Связи", "Не удалось удалить связь",
            () => _orcestrator.RemoveRelation(relation.FromId, relation.ToId));
    }

    partial void OnViewModeChanged(SourcesRelationsViewMode value)
    {
        _settings.SetValue(ViewModeSettingKey, value.ToString());
        _ = UpdateAuthenticationActivityAsync();
        OnPropertyChanged(nameof(CanHandleEscape));
    }

    private async Task TryMutateAsync(string dialogTitle, string errorPrefix, Action action)
    {
        if (TryExecuteMutation(dialogTitle, errorPrefix, action))
        {
            if (IsAuthenticationMode)
            {
                await LoadAuthenticationCatalogAsync();
            }
            await ReloadAsync();
        }
    }

    private void TryMutateAndReload(string dialogTitle, string errorPrefix, Action action)
    {
        if (TryExecuteMutation(dialogTitle, errorPrefix, action))
        {
            _ = ReloadAsync();
        }
    }

    private bool TryExecuteMutation(string dialogTitle, string errorPrefix, Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{ErrorPrefix}", errorPrefix);
            _dialogs.Error(dialogTitle, $"{errorPrefix}: {ex.Message}");
            return false;
        }
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

        var sourceStats = SourcesRelationsStatsBuilder.BuildSourceStats(sources, medias);
        var relationStats = SourcesRelationsStatsBuilder.BuildRelationStats(relations, medias);

        await _dispatcher.InvokeAsync(() => Apply(sources, relations, pluginTypes, sourceStats, relationStats));
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
            .Select(s => new SourceCardViewModel(s, this, sourceStats.GetValueOrDefault(s.Id, SourceCardStats.Empty))));

        Relations.ReplaceAll(relations.Select(r =>
            new RelationCardViewModel(r, this, relationStats.GetValueOrDefault((r.FromId, r.ToId), RelationCardStats.Empty))));

        PluginTypes.ReplaceAll(pluginTypes);
        if (!IsAuthenticationMode)
        {
            Authentication.SetSources(sources);
        }

        foreach (var name in SourcesRelationsStatsBuilder.SummaryPropertyNames)
        {
            OnPropertyChanged(name);
        }

        AddRelationCommand.NotifyCanExecuteChanged();
        StatusMessage = null;
    }

    internal void SetEmptyForAutomation(bool empty)
    {
        if (empty)
        {
            _automationSourcesStash ??= [.. Sources];
            _automationRelationsStash ??= [.. Relations];
            Sources.ReplaceAll([]);
            Relations.ReplaceAll([]);
        }
        else if (_automationSourcesStash is not null)
        {
            Sources.ReplaceAll(_automationSourcesStash);
            Relations.ReplaceAll(_automationRelationsStash ?? []);
            _automationSourcesStash = null;
            _automationRelationsStash = null;
        }

        foreach (var name in SourcesRelationsStatsBuilder.SummaryPropertyNames)
        {
            OnPropertyChanged(name);
        }

        AddRelationCommand.NotifyCanExecuteChanged();
    }
}
