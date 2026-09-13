using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaOrcestrator.Domain;
using Microsoft.Extensions.Logging;
using System.Windows.Input;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public enum AuditSyncMode
{
    None = 0,
    Full = 1,
    Quick = 2,
    New = 3,
}

public sealed partial class AuditViewModel : ObservableObject, IPageHeader, IPageRefresh
{
    public string PageTitle => "Досинк источников";

    public string? PageDescription =>
        "Запрашивает источники на новое. Полная – перечитывает всё с метаданными, быстрая – только список без деталей, новые – до первого уже известного. Bulk-кнопки блокируются, пока есть активные per-source запуски.";

    public ICommand RefreshCommand => ReloadCommand;

    public string? RefreshTooltip => "Перезагрузить список источников";


    private readonly Orcestrator _orcestrator;
    private readonly ILogger<AuditViewModel> _logger;
    private readonly object _runningLock = new();
    private readonly HashSet<string> _runningSourceIds = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;
    private int _bulkGate;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SyncAllFullCommand))]
    [NotifyCanExecuteChangedFor(nameof(SyncAllQuickCommand))]
    [NotifyCanExecuteChangedFor(nameof(SyncAllNewCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReloadCommand))]
    [NotifyPropertyChangedFor(nameof(HasAnyRunning))]
    [NotifyPropertyChangedFor(nameof(CanRunBulk))]
    [NotifyPropertyChangedFor(nameof(HasBulkProgress))]
    private bool _isBulkBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SyncAllFullCommand))]
    [NotifyCanExecuteChangedFor(nameof(SyncAllQuickCommand))]
    [NotifyCanExecuteChangedFor(nameof(SyncAllNewCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReloadCommand))]
    [NotifyPropertyChangedFor(nameof(HasAnyRunning))]
    [NotifyPropertyChangedFor(nameof(HasActiveSources))]
    [NotifyPropertyChangedFor(nameof(ActiveSourcesText))]
    [NotifyPropertyChangedFor(nameof(CanRunBulk))]
    private int _runningSourcesCount;

    [ObservableProperty]
    private string _bulkProgressMessage = "–";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBulkProgress))]
    private bool _hasBulkError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBulkProgress))]
    private bool _isBulkCancelled;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _emptyHint;

    public AuditViewModel(Orcestrator orcestrator, ILogger<AuditViewModel> logger)
    {
        _orcestrator = orcestrator;
        _logger = logger;

        Sources = [];
    }

    public RangeObservableCollection<AuditSourceCardViewModel> Sources { get; }

    public bool HasAnyRunning => IsBulkBusy || RunningSourcesCount > 0;

    public bool HasActiveSources => RunningSourcesCount > 0;

    public bool HasSources => Sources.Count > 0;

    public bool CanRunBulk => !IsBulkBusy && RunningSourcesCount == 0;

    public bool HasBulkProgress => IsBulkBusy || HasBulkError || IsBulkCancelled;

    public string ActiveSourcesText => RunningSourcesCount switch
    {
        0 => string.Empty,
        1 => "Активно: 1 источник",
        _ => $"Активно: {RunningSourcesCount} источников",
    };

    public async Task EnsureLoadedAsync()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await ReloadAsync();
    }

    [RelayCommand(CanExecute = nameof(CanRunBulk))]
    public async Task ReloadAsync()
    {
        IsLoading = true;

        try
        {
            var sources = await Task.Run(() => _orcestrator.GetSources());
            ApplySources(sources);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось загрузить список источников для досинка");
            BulkProgressMessage = $"Ошибка загрузки: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanRunBulk))]
    private Task SyncAllFull(CancellationToken cancellationToken)
    {
        return RunBulkAsync(AuditSyncMode.Full, cancellationToken);
    }

    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanRunBulk))]
    private Task SyncAllQuick(CancellationToken cancellationToken)
    {
        return RunBulkAsync(AuditSyncMode.Quick, cancellationToken);
    }

    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanRunBulk))]
    private Task SyncAllNew(CancellationToken cancellationToken)
    {
        return RunBulkAsync(AuditSyncMode.New, cancellationToken);
    }

    internal Task RunForSourceAsync(AuditSourceCardViewModel card, AuditSyncMode mode, CancellationToken cancellationToken)
    {
        return RunSourceAsync(card, mode, cancellationToken);
    }

    private async Task RunBulkAsync(AuditSyncMode mode, CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _bulkGate, 1, 0) != 0)
        {
            return;
        }

        if (RunningSourcesCount > 0)
        {
            Interlocked.Exchange(ref _bulkGate, 0);
            _logger.LogInformation("Bulk-синхронизация отклонена: уже идут per-source запуски");
            return;
        }

        IsBulkBusy = true;
        SetAllCardsBulkLocked(true);

        var (isFull, onlyNew) = MapMode(mode);
        var modeText = ModeText(mode);

        _logger.LogInformation("Запуск {Mode} bulk-синхронизации всех источников.", modeText);
        HasBulkError = false;
        IsBulkCancelled = false;
        BulkProgressMessage = "Старт…";

        var progress = new Progress<string>(message => BulkProgressMessage = message);
        var sourceProgress = BuildSourceProgressMap();
        var succeeded = false;

        try
        {
            await _orcestrator.GetStorageFullInfo(isFull, null, onlyNew, progress, sourceProgress, cancellationToken);
            succeeded = true;
        }
        catch (OperationCanceledException)
        {
            BulkProgressMessage = "Отменено";
            IsBulkCancelled = true;
            _logger.LogInformation("Bulk-синхронизация отменена пользователем.");
        }
        catch (Exception ex)
        {
            HasBulkError = true;
            BulkProgressMessage = $"Ошибка: {ex.Message}";
            _logger.LogError(ex, "Ошибка при bulk-синхронизации.");
        }
        finally
        {
            await RefreshAllSourcesAsync();
            SetAllCardsBulkLocked(false);

            if (succeeded)
            {
                BulkProgressMessage = "–";
                ClearAllCardProgress();
            }

            IsBulkBusy = false;
            Interlocked.Exchange(ref _bulkGate, 0);
        }
    }

    private async Task RunSourceAsync(AuditSourceCardViewModel card, AuditSyncMode mode, CancellationToken cancellationToken)
    {
        lock (_runningLock)
        {
            if (IsBulkBusy || !_runningSourceIds.Add(card.SourceId))
            {
                _logger.LogInformation("Per-source запуск {Source} отклонён (bulk или уже идёт)", card.Source.TitleFull);
                return;
            }
        }

        RunningSourcesCount = SnapshotRunningCount();
        card.IsBusy = true;
        card.ResetState();

        var (isFull, onlyNew) = MapMode(mode);
        var modeText = ModeText(mode);
        _logger.LogInformation("Запуск {Mode} синхронизации источника {Source}.", modeText, card.Source.TitleFull);

        card.Report("Старт…");
        var progress = new Progress<string>(message => card.Report(message));
        var succeeded = false;

        try
        {
            await _orcestrator.GetStorageFullInfo(isFull, card.Source, onlyNew, progress, null, cancellationToken);
            succeeded = true;
        }
        catch (OperationCanceledException)
        {
            card.ReportCancelled("Отменено");
            _logger.LogInformation("Синхронизация {Source} отменена пользователем.", card.Source.TitleFull);
        }
        catch (Exception ex)
        {
            card.ReportError($"Ошибка: {ex.Message}");
            _logger.LogError(ex, "Ошибка синхронизации {Source}.", card.Source.TitleFull);
        }
        finally
        {
            await RefreshSingleSourceAsync(card);

            if (succeeded)
            {
                card.ClearProgress();
            }

            card.IsBusy = false;

            lock (_runningLock)
            {
                _runningSourceIds.Remove(card.SourceId);
            }

            RunningSourcesCount = SnapshotRunningCount();
        }
    }

    private int SnapshotRunningCount()
    {
        lock (_runningLock)
        {
            return _runningSourceIds.Count;
        }
    }

    private async Task RefreshAllSourcesAsync()
    {
        try
        {
            var fresh = await Task.Run(() => _orcestrator.GetSources());
            var byId = fresh.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);

            foreach (var card in Sources)
            {
                if (byId.TryGetValue(card.SourceId, out var updated))
                {
                    card.Source.LastSyncedAt = updated.LastSyncedAt;
                    card.RefreshFromSource();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось обновить метки последней синхронизации (bulk)");
        }
    }

    private async Task RefreshSingleSourceAsync(AuditSourceCardViewModel card)
    {
        try
        {
            var fresh = await Task.Run(() => _orcestrator.GetSources());
            var updated = fresh.FirstOrDefault(s => string.Equals(s.Id, card.SourceId, StringComparison.OrdinalIgnoreCase));

            if (updated != null)
            {
                card.Source.LastSyncedAt = updated.LastSyncedAt;
                card.RefreshFromSource();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось обновить метку последней синхронизации для {Source}", card.Source.TitleFull);
        }
    }

    private void SetAllCardsBulkLocked(bool locked)
    {
        foreach (var card in Sources)
        {
            card.IsBulkLocked = locked;
        }
    }

    private void ClearAllCardProgress()
    {
        foreach (var card in Sources)
        {
            if (!card.HasError)
            {
                card.ClearProgress();
            }
        }
    }

    private Dictionary<string, IProgress<string>> BuildSourceProgressMap()
    {
        var map = new Dictionary<string, IProgress<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var card in Sources)
        {
            var local = card;
            local.Report("Ожидание…");
            map[local.SourceId] = new Progress<string>(message => local.Report(message));
        }

        return map;
    }

    private void ApplySources(List<Source> sources)
    {
        Sources.ReplaceAll(sources.Select(source => new AuditSourceCardViewModel(source, this)
        {
            IsBulkLocked = IsBulkBusy,
        }));

        EmptyHint = sources.Count == 0
            ? "Источники не настроены. Добавьте источник в разделе «Источники и связи»."
            : null;

        OnPropertyChanged(nameof(HasSources));
    }

    private static (bool isFull, bool onlyNew) MapMode(AuditSyncMode mode)
    {
        return mode switch
        {
            AuditSyncMode.Full => (true, false),
            AuditSyncMode.Quick => (false, false),
            AuditSyncMode.New => (false, true),
            _ => (false, false),
        };
    }

    private static string ModeText(AuditSyncMode mode)
    {
        return mode switch
        {
            AuditSyncMode.Full => "полной",
            AuditSyncMode.Quick => "быстрой",
            AuditSyncMode.New => "новых",
            _ => "",
        };
    }
}
