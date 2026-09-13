using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MediaOrcestrator.Runner.Wpf.ViewModels.Comments;

public sealed partial class CommentsFetchOptionsDialogViewModel : ObservableObject, IDialogViewModel
{
    private const int SinceDaysMin = 0;
    private const int SinceDaysMax = 3650;
    private const int OnlyRecentMin = 0;
    private const int OnlyRecentMax = 10000;

    [ObservableProperty]
    private int _sinceDays;

    [ObservableProperty]
    private int _onlyRecent;

    [ObservableProperty]
    private int _maxParallelism;

    public CommentsFetchOptionsDialogViewModel(CommentsViewSettings settings)
    {
        SinceDays = Math.Clamp(settings.FetchSinceDays, SinceDaysMin, SinceDaysMax);
        OnlyRecent = Math.Clamp(settings.FetchOnlyRecent, OnlyRecentMin, OnlyRecentMax);
        MaxParallelism = settings.FetchMaxParallelism <= 0
            ? CommentsViewSettings.DefaultFetchMaxParallelism
            : Math.Clamp(settings.FetchMaxParallelism, CommentsViewSettings.MinFetchMaxParallelism, CommentsViewSettings.MaxFetchMaxParallelism);
    }

    public event EventHandler<bool>? RequestClose;

    public int ParallelismMin => CommentsViewSettings.MinFetchMaxParallelism;
    public int ParallelismMax => CommentsViewSettings.MaxFetchMaxParallelism;

    [RelayCommand]
    private void Ok()
    {
        RequestClose?.Invoke(this, true);
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke(this, false);
    }

    [RelayCommand]
    private void IncreaseSinceDays() => SinceDays = Math.Clamp(SinceDays + 1, SinceDaysMin, SinceDaysMax);

    [RelayCommand]
    private void DecreaseSinceDays() => SinceDays = Math.Clamp(SinceDays - 1, SinceDaysMin, SinceDaysMax);

    [RelayCommand]
    private void IncreaseOnlyRecent() => OnlyRecent = Math.Clamp(OnlyRecent + 1, OnlyRecentMin, OnlyRecentMax);

    [RelayCommand]
    private void DecreaseOnlyRecent() => OnlyRecent = Math.Clamp(OnlyRecent - 1, OnlyRecentMin, OnlyRecentMax);

    [RelayCommand]
    private void IncreaseMaxParallelism() => MaxParallelism = Math.Clamp(MaxParallelism + 1, ParallelismMin, ParallelismMax);

    [RelayCommand]
    private void DecreaseMaxParallelism() => MaxParallelism = Math.Clamp(MaxParallelism - 1, ParallelismMin, ParallelismMax);

    partial void OnSinceDaysChanged(int value)
    {
        var clamped = Math.Clamp(value, SinceDaysMin, SinceDaysMax);

        if (clamped != value)
        {
            SinceDays = clamped;
        }
    }

    partial void OnOnlyRecentChanged(int value)
    {
        var clamped = Math.Clamp(value, OnlyRecentMin, OnlyRecentMax);

        if (clamped != value)
        {
            OnlyRecent = clamped;
        }
    }

    partial void OnMaxParallelismChanged(int value)
    {
        var clamped = Math.Clamp(value, ParallelismMin, ParallelismMax);

        if (clamped != value)
        {
            MaxParallelism = clamped;
        }
    }
}
