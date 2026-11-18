using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.Services;
using System.Collections.ObjectModel;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class SourceAuthenticationRecoveryViewModel(INavigationService? navigation) : ObservableObject
{
    public ObservableCollection<SourceOperationAuthenticationException> Failures { get; } = [];

    internal void Report(Exception exception)
    {
        if (exception is SourceOperationAuthenticationException failure
            && !Failures.Any(existing => existing.SourceId == failure.SourceId && existing.MethodId == failure.MethodId))
        {
            Failures.Add(failure);
        }
    }

    [RelayCommand]
    private Task OpenAsync(SourceOperationAuthenticationException? failure) =>
        navigation is not null && failure is not null
            ? navigation.OpenSourceAuthenticationAsync(failure)
            : Task.CompletedTask;
}
