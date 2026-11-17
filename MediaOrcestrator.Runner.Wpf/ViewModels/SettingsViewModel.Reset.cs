using CommunityToolkit.Mvvm.Input;
using MediaOrcestrator.Runner.Wpf.ViewModels.Settings;
using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public partial class SettingsViewModel
{
    public IReadOnlyDictionary<string, SettingsResetField> ResetFields => Reset.Fields;

    [RelayCommand(CanExecute = nameof(CanResetSection))]
    private void ResetSection(SettingsResetPlan? plan)
    {
        if (plan is null || !plan.CanReset)
        {
            return;
        }

        var changed = plan.Apply();
        RefreshResetState();

        if (changed.Count == 0)
        {
            return;
        }

        var names = string.Join(", ", changed.Select(field => field.Caption));
        var title = Sections[plan.SectionKey].Title;

        _logger.LogInformation("Сброшен раздел настроек «{Section}»: {Fields}", title, names);

        if (string.Equals(plan.SectionKey, SettingsSectionKeys.Paths, StringComparison.Ordinal))
        {
            var message = HasUnsavedChanges
                ? $"Заводские пути подставлены в форму ({names}) – нажмите «Сохранить», чтобы записать их в settings.toml."
                : "Несохранённые правки отменены – в settings.toml уже заводские пути.";
            _toasts.Show(message, StatusSeverity.Info);
            return;
        }

        _toasts.Show($"Раздел «{title}» сброшен: {names}.", StatusSeverity.Info);
    }

    private bool CanResetSection(SettingsResetPlan? plan) => plan?.CanReset ?? false;

    private void OnFieldReset(SettingsResetField field)
    {
        if (field.IsFactory)
        {
            return;
        }

        field.Apply();
        RefreshResetState();

        _logger.LogInformation("Сброшена настройка «{Field}» к значению «{Value}»", field.Caption, field.FactoryLabel);

        if (string.Equals(field.SectionKey, SettingsSectionKeys.Paths, StringComparison.Ordinal))
        {
            var message = HasUnsavedChanges
                ? $"В поле «{field.Caption}» подставлено значение по умолчанию ({field.FactoryLabel}) – нажмите «Сохранить», чтобы записать в settings.toml."
                : $"Несохранённые правки в «{field.Caption}» отменены – в settings.toml уже заводское значение.";
            _toasts.Show(message, StatusSeverity.Info);
            return;
        }

        _toasts.Show($"«{field.Caption}» возвращено к заводскому значению ({field.FactoryLabel}).", StatusSeverity.Info);
    }

    private void RefreshResetState()
    {
        Reset.Refresh();
        ResetSectionCommand.NotifyCanExecuteChanged();
    }
}
