using CommunityToolkit.Mvvm.ComponentModel;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.Bootstrap;
using MediaOrcestrator.Runner.Wpf.Services;
using System.ComponentModel;
using System.Windows.Input;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class OperationsViewModel : ObservableObject, IPageStatus, IPageHeader
{
    public string PageTitle => $"Операции / {SubsectionTitle}";

    public string? PageDescription => Selected switch
    {
        OperationsSubsection.Preview => "Обложки и превью пачкой: генерация из кадра, замена своим файлом, выгрузка. Набор берётся из «Медиа» или из всего хранилища.",
        OperationsSubsection.Merge => "Поиск дублей одного выпуска по названию и метаданным и слияние их в одну карточку.",
        OperationsSubsection.Conflicts => "Группы, которые слияние не берёт автоматически: разные длительности, спорные названия, конфликтующие связи.",
        _ => "Пакетное переименование по шаблону. Набор берётся из «Медиа» или из всего хранилища.",
    };

    private string SubsectionTitle => Selected switch
    {
        OperationsSubsection.Preview => "Обложки и превью",
        OperationsSubsection.Merge => "Слияние дубликатов",
        OperationsSubsection.Conflicts => "Разбор конфликтов",
        _ => "Переименование",
    };

    private const string SubsectionSettingsKey = "wpf.operations.subsection";

    private readonly ISettingsStore _settings;
    private bool _suppressPersist;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Current))]
    [NotifyPropertyChangedFor(nameof(PageTitle))]
    [NotifyPropertyChangedFor(nameof(PageDescription))]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyPropertyChangedFor(nameof(ProgressValue))]
    [NotifyPropertyChangedFor(nameof(ProgressMax))]
    [NotifyPropertyChangedFor(nameof(IsIndeterminate))]
    [NotifyPropertyChangedFor(nameof(StatusCaption))]
    [NotifyPropertyChangedFor(nameof(CancelCommand))]
    private OperationsSubsection _selected;

    public OperationsViewModel(
        BatchRenameSubViewModel rename,
        BatchPreviewSubViewModel preview,
        MergeDuplicatesSubViewModel merge,
        ResolveConflictsSubViewModel conflicts,
        ISettingsStore settings)
    {
        Rename = rename;
        Preview = preview;
        Merge = merge;
        Conflicts = conflicts;
        _settings = settings;

        Rename.PropertyChanged += OnSubPropertyChanged;
        Preview.PropertyChanged += OnSubPropertyChanged;
        Merge.PropertyChanged += OnSubPropertyChanged;
        Conflicts.PropertyChanged += OnSubPropertyChanged;

        _suppressPersist = true;
        try
        {
            Selected = ParseStored(settings.GetStringValue(SubsectionSettingsKey));
        }
        finally
        {
            _suppressPersist = false;
        }
    }

    public BatchRenameSubViewModel Rename { get; }

    public BatchPreviewSubViewModel Preview { get; }

    public MergeDuplicatesSubViewModel Merge { get; }

    public ResolveConflictsSubViewModel Conflicts { get; }

    public object Current => Selected switch
    {
        OperationsSubsection.Preview => Preview,
        OperationsSubsection.Merge => Merge,
        OperationsSubsection.Conflicts => Conflicts,
        _ => Rename,
    };

    public bool IsBusy => Current switch
    {
        BatchRenameSubViewModel r => r.IsApplying,
        BatchPreviewSubViewModel p => p.IsApplying,
        MergeDuplicatesSubViewModel m => m.IsApplying || m.IsSearching,
        ResolveConflictsSubViewModel c => c.IsApplying || c.IsSearching,
        _ => false,
    };

    public bool IsIndeterminate => false;

    public double ProgressValue => Current switch
    {
        BatchRenameSubViewModel r => r.ProgressValue,
        BatchPreviewSubViewModel p => p.ProgressValue,
        MergeDuplicatesSubViewModel m => m.ProgressValue,
        ResolveConflictsSubViewModel c => c.ProgressValue,
        _ => 0,
    };

    public double ProgressMax => Current switch
    {
        BatchRenameSubViewModel r => r.ProgressMax,
        BatchPreviewSubViewModel p => p.ProgressMax,
        MergeDuplicatesSubViewModel m => m.ProgressMax,
        ResolveConflictsSubViewModel c => c.ProgressMax,
        _ => 1,
    };

    public string? StatusCaption => Current switch
    {
        BatchRenameSubViewModel r => r.StatusText,
        BatchPreviewSubViewModel p => p.StatusText,
        MergeDuplicatesSubViewModel m => m.StatusText,
        ResolveConflictsSubViewModel c => c.StatusText,
        _ => null,
    };

    public ICommand? CancelCommand => Current switch
    {
        BatchRenameSubViewModel r => r.CancelCommand,
        BatchPreviewSubViewModel p => p.CancelCommand,
        MergeDuplicatesSubViewModel m => m.CancelCommand,
        ResolveConflictsSubViewModel c => c.CancelCommand,
        _ => null,
    };

    public void ApplyExternalSelection(OperationsTab tab, IReadOnlyList<Media> selection)
    {
        switch (tab)
        {
            case OperationsTab.Rename:
                Selected = OperationsSubsection.Rename;

                if (selection.Count > 0)
                {
                    Rename.LoadMedia(selection);
                }

                break;

            case OperationsTab.Preview:
                Selected = OperationsSubsection.Preview;

                if (selection.Count > 0)
                {
                    Preview.LoadMedia(selection);
                }

                break;

            case OperationsTab.Merge:
                Selected = OperationsSubsection.Merge;
                Merge.EnsureScopeLoaded();
                break;

            case OperationsTab.Conflicts:
                Selected = OperationsSubsection.Conflicts;
                Conflicts.EnsureScopeLoaded();
                break;
        }
    }

    partial void OnSelectedChanged(OperationsSubsection value)
    {
        if (_suppressPersist)
        {
            return;
        }

        _settings.SetValue(SubsectionSettingsKey, value.ToString());
    }

    private void OnSubPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, Current))
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(BatchRenameSubViewModel.IsApplying):
            case nameof(MergeDuplicatesSubViewModel.IsSearching):
                OnPropertyChanged(nameof(IsBusy));
                break;
            case nameof(BatchRenameSubViewModel.ProgressValue):
                OnPropertyChanged(nameof(ProgressValue));
                break;
            case nameof(BatchRenameSubViewModel.ProgressMax):
                OnPropertyChanged(nameof(ProgressMax));
                break;
            case nameof(BatchRenameSubViewModel.StatusText):
                OnPropertyChanged(nameof(StatusCaption));
                break;
        }
    }

    private static OperationsSubsection ParseStored(string? raw)
    {
        return Enum.TryParse<OperationsSubsection>(raw, ignoreCase: true, out var parsed)
            ? parsed
            : OperationsSubsection.Rename;
    }
}
