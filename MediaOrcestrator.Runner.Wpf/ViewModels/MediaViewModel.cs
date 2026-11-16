using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.Bootstrap;
using MediaOrcestrator.Runner.Wpf.MediaContextMenu;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Media;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class MediaViewModel : ObservableObject, IMediaActionUi, IPageStatus, IPageHeader, IPageRefresh, IPageRenderMetric, IPageEscapeHandler, IPageSearchable
{
    public string PageTitle => "Медиа";

    public string? PageDescription =>
        "Все медиа со всех источников в едином списке. Поиск, фильтры по источникам, статусам и связям, колонки метаданных, быстрые действия в контекстном меню и в панели справа.";

    public ICommand RefreshCommand => ReloadCommand;

    public string? RefreshTooltip => "Перезагрузить медиа из БД";

    string? IPageRenderMetric.RenderDurationText => RenderDurationText;

    IReadOnlyList<PageRenderStage>? IPageRenderMetric.RenderStages => RenderStages;

    private const string InspectorCollapsedSettingKey = "wpf.media.inspector.collapsed";
    private const string InspectorWidthSettingKey = "wpf.media.inspector.width";

    public const double DefaultInspectorWidth = 480;
    public const double MinInspectorWidth = 260;
    private const double MaxInspectorWidth = 1000;

    private readonly Orcestrator _orcestrator;
    private readonly ActionHolder _actionHolder;
    private readonly ISettingsStore _settings;
    private readonly ILogger<MediaViewModel> _logger;
    private readonly List<Source> _allSources = [];
    private readonly List<Media> _allMedias = [];
    private readonly List<SourceSyncRelation> _allRelations = [];

    private double _inspectorWidth = DefaultInspectorWidth;
    private bool _loaded;
    private bool _suppressPrefPersist;

    public MediaViewModel(
        Orcestrator orcestrator,
        ActionHolder actionHolder,
        ISettingsStore settings,
        MediaActionContextFactory actionContextFactory,
        MediaContextMenuBuilder contextMenuBuilder,
        ILoggerFactory loggerFactory,
        ILogger<MediaViewModel> logger,
        IShellLauncher shellLauncher,
        IClipboardService clipboard)
    {
        _orcestrator = orcestrator;
        _actionHolder = actionHolder;
        _settings = settings;
        _logger = logger;

        Inspector = new(loggerFactory.CreateLogger<MediaInspectorViewModel>(), settings, shellLauncher, clipboard);
        Filter = new(settings);
        Grid = new(Filter, Inspector, actionContextFactory, this, contextMenuBuilder, settings, () => _allSources)
        {
            PublishMetric = PublishRenderMetric,
        };

        Grid.RequestExpandInspector += (_, _) => IsInspectorCollapsed = false;

        LoadViewPreferences();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InspectorToggleTooltip))]
    [NotifyPropertyChangedFor(nameof(InspectorToggleAutomationName))]
    [NotifyPropertyChangedFor(nameof(CanHandleEscape))]
    private bool _isInspectorCollapsed;

    [ObservableProperty]
    private string? _renderDurationText;

    [ObservableProperty]
    private IReadOnlyList<PageRenderStage>? _renderStages;

    [ObservableProperty]
    private string? _statusCaption;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isIndeterminate;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private double _progressMax = 100;

    private CancellationTokenSource? _convertCts;

    public event EventHandler? SearchFocusRequested;

    public bool CanHandleEscape => !IsInspectorCollapsed;

    public bool TryHandleEscape()
    {
        if (IsInspectorCollapsed)
        {
            return false;
        }

        IsInspectorCollapsed = true;
        return true;
    }

    public void RequestSearchFocus() => SearchFocusRequested?.Invoke(this, EventArgs.Empty);

    public MediaFilterViewModel Filter { get; }

    public MediaGridViewModel Grid { get; }

    public MediaInspectorViewModel Inspector { get; }

    public string InspectorToggleTooltip => IsInspectorCollapsed
        ? "Развернуть панель деталей справа"
        : "Свернуть панель деталей справа";

    public string InspectorToggleAutomationName => IsInspectorCollapsed
        ? "Развернуть панель деталей"
        : "Свернуть панель деталей";

    public double InspectorWidth => _inspectorWidth;

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
        Grid.IsLoading = true;
        Grid.LoadError = null;

        using var cts = new CancellationTokenSource();
        var action = _actionHolder.Register("Загрузка медиа", "Чтение БД…", 0, cts, kind: ActionKind.Other);

        MediaReloadBreakdown? breakdown = null;

        try
        {
            breakdown = await Task.Run(LoadFromDomainAsync, cts.Token);
            action.Finish($"Загружено: {_allMedias.Count}");
        }
        catch (OperationCanceledException)
        {
            action.MarkCancelled("Отменено");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось загрузить медиа");
            action.Fail("Ошибка: " + ex.Message, ex);
            Grid.LoadError = ex.Message;
        }
        finally
        {
            Grid.IsLoading = false;
        }

        if (breakdown is { } b)
        {
            var build = b.Build;
            var vmMs = b.DbMs + b.FiltersMs + build.ColumnsMs + build.RowsMs + build.ViewMs;
            PublishRenderMetric(
            [
                new("Чтение БД", FormatMs(b.DbMs)),
                new("Подготовка фильтров", FormatMs(b.FiltersMs)),
                new("Перестроение колонок", FormatMs(build.ColumnsMs)),
                new("Сборка строк", FormatMs(build.RowsMs)),
                new("Обновление таблицы", FormatMs(build.ViewMs)),
            ], vmMs);
        }
    }

    [RelayCommand]
    private void ToggleInspectorCollapsed()
    {
        IsInspectorCollapsed = !IsInspectorCollapsed;
    }

    void IMediaActionUi.SetLoading(bool isLoading)
    {
        Dispatch(() => Grid.IsLoading = isLoading);
    }

    void IMediaActionUi.NotifyDataChanged()
    {
        _ = ReloadAsync();
    }

    public ICommand? CancelCommand => CancelConvertCommand;

    [RelayCommand(CanExecute = nameof(CanCancelConvert))]
    private void CancelConvert()
    {
        _convertCts?.Cancel();
    }

    private bool CanCancelConvert() => _convertCts is { IsCancellationRequested: false };

    void IMediaActionUi.ShowConvertProgress(double percent, string text)
    {
        Dispatch(() =>
        {
            IsBusy = true;
            IsIndeterminate = false;
            ProgressMax = 100;
            ProgressValue = Math.Clamp(percent, 0, 100);
            StatusCaption = text;
        });
    }

    void IMediaActionUi.HideConvertProgress()
    {
        Dispatch(() =>
        {
            IsBusy = false;
            ProgressValue = 0;
            StatusCaption = null;
            _convertCts = null;
            CancelConvertCommand.NotifyCanExecuteChanged();
        });
    }

    void IMediaActionUi.RegisterConvertCancellation(CancellationTokenSource? cts)
    {
        Dispatch(() =>
        {
            _convertCts = cts;
            CancelConvertCommand.NotifyCanExecuteChanged();
        });
    }

    public void SetInspectorWidth(double width)
    {
        if (double.IsNaN(width) || width <= 0)
        {
            return;
        }

        var clamped = Math.Clamp(width, MinInspectorWidth, MaxInspectorWidth);

        if (Math.Abs(clamped - _inspectorWidth) < 0.5)
        {
            return;
        }

        _inspectorWidth = clamped;
        OnPropertyChanged(nameof(InspectorWidth));

        if (!_suppressPrefPersist)
        {
            _settings.SetValue(InspectorWidthSettingKey, clamped.ToString("F0", System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    partial void OnIsInspectorCollapsedChanged(bool value)
    {
        if (!_suppressPrefPersist)
        {
            _settings.SetValue(InspectorCollapsedSettingKey, value ? "true" : "false");
        }
    }

    private async Task<MediaReloadBreakdown> LoadFromDomainAsync()
    {
        var swDb = Stopwatch.StartNew();
        var sources = _orcestrator.GetSources();
        var medias = _orcestrator.GetMedias();
        var relations = _orcestrator.GetRelations();
        swDb.Stop();

        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null)
        {
            return new(swDb.ElapsedMilliseconds, 0, new(0, 0, 0));
        }

        var apply = await dispatcher.InvokeAsync(() => ApplyDomainData(sources, medias, relations));
        var (filtersMs, build) = apply;
        return new(swDb.ElapsedMilliseconds, filtersMs, build);
    }

    private (long FiltersMs, MediaGridViewModel.MediaRebuildBreakdown Build) ApplyDomainData(List<Source> sources, List<Media> medias, List<SourceSyncRelation> relations)
    {
        _allSources.Clear();
        _allSources.AddRange(sources);
        _allMedias.Clear();
        _allMedias.AddRange(medias);
        _allRelations.Clear();
        _allRelations.AddRange(relations);

        var swFilters = Stopwatch.StartNew();
        Filter.ApplyDomainData(sources, medias, relations);
        swFilters.Stop();

        var build = Grid.ApplyDomainData(medias);

        return (swFilters.ElapsedMilliseconds, build);
    }

    private void PublishRenderMetric(IReadOnlyList<PageRenderStage> vmStages, long vmMs)
    {
        if (System.Windows.Application.Current?.Dispatcher is null)
        {
            ApplyMetric(vmStages, vmMs, renderMs: 0);
            return;
        }

        var renderSw = Stopwatch.StartNew();
        EventHandler? handler = null;

        handler = (_, _) =>
        {
            CompositionTarget.Rendering -= handler;
            renderSw.Stop();
            ApplyMetric(vmStages, vmMs, renderSw.ElapsedMilliseconds);
        };

        CompositionTarget.Rendering += handler;
    }

    private void ApplyMetric(IReadOnlyList<PageRenderStage> vmStages, long vmMs, long renderMs)
    {
        var totalMs = vmMs + renderMs;

        var stages = new List<PageRenderStage>(vmStages.Count + 5);
        stages.AddRange(vmStages);
        stages.Add(new("Отрисовка кадра", FormatMs(renderMs)));
        stages.Add(new("Итого", FormatMs(totalMs)));
        stages.Add(new("Строк", Grid.Rows.Count.ToString()));
        stages.Add(new("Колонок-источников", Grid.ColumnSources.Count.ToString()));
        stages.Add(new("Колонок метаданных", Grid.MetadataColumns.Count.ToString()));

        RenderDurationText = FormatMs(totalMs);
        RenderStages = stages;
    }

    private static string FormatMs(long ms) => $"{ms} мс";

    private sealed record MediaReloadBreakdown(long DbMs, long FiltersMs, MediaGridViewModel.MediaRebuildBreakdown Build);

    private void LoadViewPreferences()
    {
        _suppressPrefPersist = true;

        try
        {
            if (bool.TryParse(_settings.GetStringValue(InspectorCollapsedSettingKey), out var collapsed))
            {
                IsInspectorCollapsed = collapsed;
            }

            var rawWidth = _settings.GetStringValue(InspectorWidthSettingKey);

            if (!string.IsNullOrWhiteSpace(rawWidth)
                && double.TryParse(rawWidth, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var width))
            {
                _inspectorWidth = Math.Clamp(width, MinInspectorWidth, MaxInspectorWidth);
            }
        }
        finally
        {
            _suppressPrefPersist = false;
        }
    }

    private static void Dispatch(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }
}

public sealed class PrimaryActionGroup
{
    public required MahApps.Metro.IconPacks.PackIconLucideKind Icon { get; init; }

    public Brush? Brush { get; init; }

    public required string Label { get; init; }

    public required IReadOnlyList<MediaMenuItemViewModel> Choices { get; init; }

    public bool IsSingle => Choices.Count == 1;

    public MediaMenuItemViewModel? First => Choices.Count > 0 ? Choices[0] : null;
}

public sealed record MultiSourceTally(string SourceTitle, int Present, int Total, int Errors)
{
    public double Fraction => Total > 0 ? (double)Present / Total : 0;

    public string CountText => $"{Present}/{Total}";

    public bool HasErrors => Errors > 0;
}
