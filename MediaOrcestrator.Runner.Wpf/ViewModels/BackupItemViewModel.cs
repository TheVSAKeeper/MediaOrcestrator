using CommunityToolkit.Mvvm.ComponentModel;
using MediaOrcestrator.Domain;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class BackupItemViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isDeleting;

    public required string FileName { get; init; }

    public required DateTime CreatedAt { get; init; }

    public required BackupTrigger Trigger { get; init; }

    public required long SizeBytes { get; init; }

    public string CreatedAtText => CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");

    public string SizeText => FormatBytes(SizeBytes);

    public string TriggerText => Trigger switch
    {
        BackupTrigger.Startup => "при старте",
        BackupTrigger.Scheduled => "по расписанию",
        BackupTrigger.Manual => "вручную",
        _ => "–",
    };

    private static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            <= 0 => "0 Б",
            < 1024 => $"{bytes} Б",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} КБ",
            < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} МБ",
            _ => $"{bytes / (1024.0 * 1024 * 1024):F1} ГБ",
        };
    }
}
