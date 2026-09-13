using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaOrcestrator.Domain;
using System.Collections.ObjectModel;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class AddRelationViewModel : ObservableObject, IDialogViewModel
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyPropertyChangedFor(nameof(IsSameSource))]
    private Source? _from;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyPropertyChangedFor(nameof(IsSameSource))]
    private Source? _to;

    public AddRelationViewModel(IEnumerable<Source> sources)
    {
        Sources = new(sources.OrderBy(s => s.Title, StringComparer.CurrentCultureIgnoreCase));
    }

    public event EventHandler<bool>? RequestClose;

    public ObservableCollection<Source> Sources { get; }

    public bool IsSameSource => From is not null && To is not null && From.Id == To.Id;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        RequestClose?.Invoke(this, true);
    }

    [RelayCommand]
    private void Cancel()
    {
        From = null;
        To = null;
        RequestClose?.Invoke(this, false);
    }

    private bool CanSave()
    {
        return From is not null && To is not null && From.Id != To.Id;
    }
}
