using CommunityToolkit.Mvvm.Input;
using MediaOrcestrator.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class SourcesRelationsViewModel
{
    private bool _viewVisible;
    private Task<bool>? _loadingAuthenticationCatalog;

    public bool CanHandleEscape => IsAuthenticationMode && Authentication.CanCloseDetails;

    public bool TryHandleEscape() => IsAuthenticationMode && Authentication.CloseDetails();

    internal void SetViewVisible(bool visible)
    {
        _viewVisible = visible;
        _ = UpdateAuthenticationActivityAsync();
    }

    internal async Task OpenAuthenticationAsync(SourceOperationAuthenticationException failure, string? originTitle, Action returnToOrigin)
    {
        ViewMode = SourcesRelationsViewMode.Authentication;
        if (originTitle is not null)
        {
            Authentication.SetReturnLink(originTitle, returnToOrigin);
        }

        if (await LoadAuthenticationCatalogAsync())
        {
            await _dispatcher.InvokeAsync(() => Authentication.FocusSourceAsync(failure));
        }
    }

    [RelayCommand]
    private async Task RefreshAuthenticationAsync(CancellationToken cancellationToken)
    {
        if (await LoadAuthenticationCatalogAsync())
        {
            await _dispatcher.InvokeAsync(() => Authentication.CheckAllAsync(cancellationToken));
        }
    }

    private Task<bool> LoadAuthenticationCatalogAsync() =>
        _loadingAuthenticationCatalog is { IsCompleted: false } loading
            ? loading
            : _loadingAuthenticationCatalog = ReadAuthenticationCatalogAsync();

    private async Task<bool> ReadAuthenticationCatalogAsync()
    {
        Authentication.IsCatalogLoading = true;
        Authentication.CatalogError = null;
        try
        {
            var sources = await Task.Run(_orcestrator.GetSources);
            return await _dispatcher.InvokeAsync(() =>
            {
                Authentication.IsCatalogLoading = false;
                Authentication.SetSources(sources);
                return true;
            });
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось загрузить источники для проверки авторизации");
            try
            {
                await _dispatcher.InvokeAsync(() =>
                {
                    Authentication.IsCatalogLoading = false;
                    Authentication.CatalogError = "Не удалось загрузить источники. Обновите список.";
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception dispatcherError)
            {
                _logger.LogError(dispatcherError, "Не удалось отобразить ошибку загрузки источников");
            }

            return false;
        }
    }

    private async Task UpdateAuthenticationActivityAsync()
    {
        try
        {
            if (!_viewVisible || !IsAuthenticationMode)
            {
                Authentication.SetActive(false);
                return;
            }

            if (await LoadAuthenticationCatalogAsync())
            {
                await _dispatcher.InvokeAsync(() =>
                {
                    if (_viewVisible && IsAuthenticationMode)
                    {
                        Authentication.SetActive(true);
                    }
                });
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось открыть вкладку авторизации");
        }
    }

    [RelayCommand]
    private Task EditAuthenticationSourceAsync(Source? source) =>
        source is null ? Task.CompletedTask : EditSource(new SourceCardViewModel(source, this, SourceCardStats.Empty));

    internal async Task EditSource(SourceCardViewModel card)
    {
        if (ResolvePluginFor(card.Source) is not { } plugin)
        {
            return;
        }

        var source = card.Source;
        await Authentication.PauseForEditAsync(source.Id);
        try
        {
            var dialogVm = _services.GetRequiredService<SourceSettingsViewModel>();
            dialogVm.InitializeEdit(source, plugin, _orcestrator.GetSources());

            if (!await _dialogs.ShowAsync(dialogVm) || dialogVm.Settings is null)
            {
                if (IsAuthenticationMode)
                {
                    await LoadAuthenticationCatalogAsync();
                }
                return;
            }

            await TryMutateAsync("Источники", "Не удалось сохранить источник", () =>
            {
                _orcestrator.UpdateSource(new Source
                {
                    Id = source.Id,
                    TypeId = source.TypeId,
                    IsDisable = source.IsDisable,
                    LastSyncedAt = source.LastSyncedAt,
                    Settings = dialogVm.Settings,
                });
                Authentication.Invalidate(source.Id);
                _logger.LogInformation("Обновлён источник {Title}", card.Title);
            });
        }
        finally
        {
            Authentication.ResumeAfterEdit(source.Id);
        }
    }
}
