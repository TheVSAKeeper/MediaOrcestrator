using MediaOrcestrator.Modules;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using System.IO;
using System.Windows;

namespace MediaOrcestrator.Runner.Wpf.Services;

public sealed class WpfAuthUI(IDialogService dialogs, ILogger<WpfAuthUI> logger) : IAuthUI
{
    public Task<string?> PromptInputAsync(string prompt, bool isPassword = false)
    {
        Application.Current.Dispatcher.Invoke(() =>
            dialogs.Info("Авторизация", prompt + "\n\nЗначение нужно ввести в поле настроек источника."));
        return Task.FromResult<string?>(null);
    }

    public async Task<string?> OpenBrowserAsync(string url, string? existingStatePath = null)
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = false,
            Args = ["--disable-blink-features=AutomationControlled"],
        });

        var contextOptions = new BrowserNewContextOptions();

        if (!string.IsNullOrEmpty(existingStatePath) && File.Exists(existingStatePath))
        {
            contextOptions.StorageStatePath = existingStatePath;
        }
        else if (!string.IsNullOrEmpty(existingStatePath))
        {
            var directory = Path.GetDirectoryName(existingStatePath);

            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        await using var context = await browser.NewContextAsync(contextOptions);
        var page = await context.NewPageAsync();

        logger.LogInformation("Открытие {Url} для авторизации", url);
        await page.GotoAsync(url, new()
        {
            Timeout = 0,
        });

        var confirmed = Application.Current.Dispatcher.Invoke(() =>
            dialogs.Confirm(
                "Сохранить авторизацию?",
                "Зайдите в свой профиль в открывшемся браузере и нажмите Да. " +
                "Если передумали – нажмите Нет.",
                defaultYes: true));

        if (!confirmed || string.IsNullOrEmpty(existingStatePath))
        {
            return null;
        }

        await context.StorageStateAsync(new()
        {
            Path = existingStatePath,
        });

        logger.LogInformation("Auth state сохранён: {Path}", existingStatePath);
        return existingStatePath;
    }

    public Task ShowMessageAsync(string message)
    {
        Application.Current.Dispatcher.Invoke(() => dialogs.Info("Авторизация", message));
        return Task.CompletedTask;
    }
}
