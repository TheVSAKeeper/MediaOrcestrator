using MediaOrcestrator.Domain;
using MediaOrcestrator.Domain.Migration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System.Diagnostics;
using System.IO;

namespace MediaOrcestrator.Runner.Wpf.Bootstrap;

internal static class StartupImportCoordinator
{
    public static bool WaitForPreviousProcessExit(int pid, TimeSpan timeout)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited)
            {
                return true;
            }

            return process.WaitForExit(timeout);
        }
        catch
        {
            return false;
        }
    }

    public static void HandlePostStartupOutcomeOrPrompt(
        IServiceProvider services,
        WinFormsPendingImportOutcome? outcome,
        bool hasUpdaterErrors,
        bool isApplyImport,
        bool hadLegacySettingsTxt)
    {
        if (outcome is not null)
        {
            ShowOutcome(services, outcome);
            return;
        }

        if (hasUpdaterErrors)
        {
            return;
        }

        var settingsStore = services.GetRequiredService<ISettingsStore>();
        var orcestrator = services.GetRequiredService<Orcestrator>();
        var settingsTomlPath = Path.Combine(AppContext.BaseDirectory, TomlSettingsFile.PrimaryFileName);

        if (FirstRunImportDetector.ShouldOpenOnStartup(
                isPrimaryInstance: true,
                isGallery: false,
                isApplyImport,
                hadLegacySettingsTxt,
                settingsStore,
                orcestrator,
                settingsTomlPath))
        {
            var openTask = services.GetRequiredService<Services.IWinFormsImportLauncher>().OpenAsync(isFirstRunPrompt: true);
            _ = openTask.ContinueWith(
                t => Log.Error(t.Exception, "Не удалось открыть мастер переноса из WinForms при первом запуске"),
                TaskContinuationOptions.OnlyOnFaulted);
        }
    }

    private static void ShowOutcome(IServiceProvider services, WinFormsPendingImportOutcome outcome)
    {
        try
        {
            if (outcome.Status == WinFormsPendingImportOutcomeStatus.Applied)
            {
                services.GetRequiredService<ToastHostViewModel>().Show(
                    "Данные WinForms перенесены. Прежняя копия сохранена в backups/.",
                    StatusSeverity.Success,
                    TimeSpan.FromSeconds(8));
                return;
            }

            var details = string.IsNullOrWhiteSpace(outcome.BackupDirectory)
                ? outcome.Message
                : $"{outcome.Message}\n\nРезервная копия сохранена в:\n{outcome.BackupDirectory}";

            services.GetRequiredService<IDialogService>().Warning("Перенос из WinForms не применился", details);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Не удалось показать итог отложенного импорта");
        }
    }
}
