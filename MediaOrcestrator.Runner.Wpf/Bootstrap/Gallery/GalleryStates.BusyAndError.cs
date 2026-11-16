using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.ViewModels;
using MediaOrcestrator.Runner.Wpf.ViewModels.Comments;
using Microsoft.Extensions.DependencyInjection;

namespace MediaOrcestrator.Runner.Wpf.Bootstrap.Gallery;

internal static partial class GalleryStates
{
    private static void ApplyBusy(string page, IServiceProvider services)
    {
        switch (page)
        {
            case "media":
                services.GetRequiredService<MediaViewModel>()
                    .SetBusyForAutomation(true, BusyValue, BusyMax, "Конвертация медиа (42 %)");
                RegisterBusyAction(services, "Конвертация медиа", "42/100");
                break;

            case "audit":
                services.GetRequiredService<AuditViewModel>().SetBusyForAutomation(true);
                RegisterBusyAction(services, "Досинк источников", "2/4");
                break;

            case "transfers":
                var transfers = services.GetRequiredService<TransfersViewModel>();
                transfers.IsRunning = true;
                transfers.RunStepsTotal = BusyMax;
                transfers.RunStepsCompleted = BusyValue;
                transfers.RunStepsFailed = 0;
                transfers.StatusText = "Передача 42/100 шагов…";
                RegisterBusyAction(services, "Перенос медиа", "42/100");
                break;

            case "publish":
                var publish = services.GetRequiredService<PublishViewModel>();
                publish.IsPublishing = true;
                publish.IsStatusError = false;
                publish.StatusMessage = "Публикация в «YouTube: Хроники хранителя»…";
                RegisterBusyAction(services, "Публикация видео", "42 %");
                break;

            case "rename":
                var rename = services.GetRequiredService<BatchRenameSubViewModel>();
                rename.IsApplying = true;
                rename.ProgressMax = BusyMax;
                rename.ProgressValue = BusyValue;
                rename.StatusText = "Переименование 42/100…";
                RegisterBusyAction(services, "Пакетное переименование", "42/100");
                break;

            case "preview":
                var preview = services.GetRequiredService<BatchPreviewSubViewModel>();
                preview.IsApplying = true;
                preview.ProgressMax = BusyMax;
                preview.ProgressValue = BusyValue;
                preview.StatusText = "Обновление обложек 42/100…";
                RegisterBusyAction(services, "Обновление превью", "42/100");
                break;

            case "merge":
                var merge = services.GetRequiredService<MergeDuplicatesSubViewModel>();
                merge.IsApplying = true;
                merge.ProgressMax = BusyMax;
                merge.ProgressValue = BusyValue;
                merge.StatusText = "Объединение дубликатов 42/100…";
                RegisterBusyAction(services, "Слияние дубликатов", "42/100");
                break;

            case "conflicts":
                var conflicts = services.GetRequiredService<ResolveConflictsSubViewModel>();
                conflicts.IsApplying = true;
                conflicts.ProgressMax = BusyMax;
                conflicts.ProgressValue = BusyValue;
                conflicts.StatusText = "Разбор конфликтов 42/100…";
                RegisterBusyAction(services, "Разбор конфликтов", "42/100");
                break;

            case "comments":
                var comments = services.GetRequiredService<CommentsViewModel>();
                comments.IsFetching = true;
                comments.FetchProgressMax = BusyMax;
                comments.FetchProgressValue = BusyValue;
                comments.FetchProgressIsIndeterminate = false;
                comments.FetchProgressMessage = "Загрузка комментариев…";
                comments.FetchCounterText = "42 / 100";
                RegisterBusyAction(services, "Загрузка комментариев", "42/100");
                break;

            case "tools":
                var tools = services.GetRequiredService<ToolsViewModel>();
                tools.IsBusy = true;
                tools.StatusMessage = "Обновление ffmpeg… 42 %";
                if (tools.Items.Count > 0)
                {
                    tools.Items[0].IsBusy = true;
                    tools.Items[0].Progress = BusyValue;
                }

                RegisterBusyAction(services, "Обновление ffmpeg", "42 %");
                break;

            case "cleanup":
                var database = services.GetRequiredService<DatabaseCleanupViewModel>();
                database.IsBusy = true;
                database.ProgressMax = BusyMax;
                database.ProgressValue = BusyValue;
                database.StatusText = "Очистка коллекций (42/100)…";
                RegisterBusyAction(services, "Очистка базы данных", "42/100");
                break;

            case "tasks":
                RegisterBusyAction(services, "Синхронизация медиатеки", "Загрузка метаданных…");
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(page), page, "У этой страницы нет занятого состояния.");
        }
    }

    private static void ResetBusy(string page, IServiceProvider services)
    {
        switch (page)
        {
            case "media":
                services.GetRequiredService<MediaViewModel>().SetBusyForAutomation(false);
                break;

            case "audit":
                services.GetRequiredService<AuditViewModel>().SetBusyForAutomation(false);
                break;

            case "transfers":
                var transfers = services.GetRequiredService<TransfersViewModel>();
                transfers.IsRunning = false;
                transfers.RunStepsTotal = 0;
                transfers.RunStepsCompleted = 0;
                transfers.StatusText = "Готов";
                break;

            case "publish":
                var publish = services.GetRequiredService<PublishViewModel>();
                publish.IsPublishing = false;
                publish.IsStatusError = false;
                publish.StatusMessage = "Готов к публикации.";
                break;

            case "rename":
                var rename = services.GetRequiredService<BatchRenameSubViewModel>();
                rename.IsApplying = false;
                rename.ProgressValue = 0;
                rename.ProgressMax = 1;
                rename.StatusText = "Готов";
                break;

            case "preview":
                var preview = services.GetRequiredService<BatchPreviewSubViewModel>();
                preview.IsApplying = false;
                preview.ProgressValue = 0;
                preview.ProgressMax = 1;
                preview.StatusText = "Готов";
                break;

            case "merge":
                var merge = services.GetRequiredService<MergeDuplicatesSubViewModel>();
                merge.IsApplying = false;
                merge.ProgressValue = 0;
                merge.ProgressMax = 1;
                merge.StatusText = "Готов";
                break;

            case "conflicts":
                var conflicts = services.GetRequiredService<ResolveConflictsSubViewModel>();
                conflicts.IsApplying = false;
                conflicts.ProgressValue = 0;
                conflicts.ProgressMax = 1;
                conflicts.StatusText = "Готов";
                break;

            case "comments":
                var comments = services.GetRequiredService<CommentsViewModel>();
                comments.IsFetching = false;
                comments.FetchProgressValue = 0;
                comments.FetchProgressMax = 0;
                comments.FetchProgressMessage = null;
                comments.FetchCounterText = null;
                break;

            case "tools":
                var tools = services.GetRequiredService<ToolsViewModel>();
                tools.IsBusy = false;
                tools.StatusMessage = string.Empty;
                if (tools.Items.Count > 0)
                {
                    tools.Items[0].IsBusy = false;
                    tools.Items[0].Progress = 0;
                }

                break;

            case "cleanup":
                var database = services.GetRequiredService<DatabaseCleanupViewModel>();
                database.IsBusy = false;
                database.ProgressValue = 0;
                database.ProgressMax = 1;
                database.StatusText = null;
                break;
        }
    }

    private static void ApplyError(string page, IServiceProvider services)
    {
        switch (page)
        {
            case "media":
                services.GetRequiredService<MediaViewModel>().Grid.LoadError =
                    "Не удалось загрузить медиатеку: файл базы данных повреждён или занят.";
                break;

            case "audit":
                services.GetRequiredService<AuditViewModel>().SetErrorForAutomation(true);
                break;

            case "transfers":
                services.GetRequiredService<TransfersViewModel>().SetErrorForAutomation(true);
                break;

            case "publish":
                var publish = services.GetRequiredService<PublishViewModel>();
                publish.IsPublishing = false;
                publish.IsStatusError = true;
                publish.StatusMessage = "Ошибка: квота загрузки на YouTube исчерпана.";
                break;

            case "comments":
                var comments = services.GetRequiredService<CommentsViewModel>();
                comments.IsLoading = false;
                comments.IsFetching = false;
                comments.LoadError = "Не удалось загрузить комментарии: превышен лимит запросов к API.";
                break;

            case "cleanup":
                var database = services.GetRequiredService<DatabaseCleanupViewModel>();
                database.IsBusy = false;
                database.StatusText = "Ошибка: база данных заблокирована другим процессом.";
                if (database.DangerItems.Count > 0)
                {
                    database.DangerItems[0].State = MaintenanceItemState.Error;
                    database.DangerItems[0].ResultText = "Ошибка: доступ запрещён";
                }

                break;

            case "tasks":
                var tasks = services.GetRequiredService<TasksViewModel>();
                _savedTasksCompletedExpanded = tasks.IsCompletedExpanded;
                tasks.IsCompletedExpanded = true;
                RegisterFailedAction(services, "Публикация в YouTube", "Квота API исчерпана");
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(page), page, "У этой страницы нет состояния ошибки.");
        }
    }

    private static void ResetError(string page, IServiceProvider services)
    {
        switch (page)
        {
            case "media":
                services.GetRequiredService<MediaViewModel>().Grid.LoadError = null;
                break;

            case "audit":
                services.GetRequiredService<AuditViewModel>().SetErrorForAutomation(false);
                break;

            case "transfers":
                services.GetRequiredService<TransfersViewModel>().SetErrorForAutomation(false);
                break;

            case "publish":
                var publish = services.GetRequiredService<PublishViewModel>();
                publish.IsStatusError = false;
                publish.StatusMessage = "Готов к публикации.";
                break;

            case "comments":
                services.GetRequiredService<CommentsViewModel>().LoadError = null;
                break;

            case "cleanup":
                var database = services.GetRequiredService<DatabaseCleanupViewModel>();
                database.StatusText = null;
                if (database.DangerItems.Count > 0)
                {
                    database.DangerItems[0].State = MaintenanceItemState.Idle;
                    database.DangerItems[0].ResultText = null;
                }

                break;

            case "tasks":
                services.GetRequiredService<TasksViewModel>().IsCompletedExpanded = _savedTasksCompletedExpanded;
                break;
        }
    }

    private static void RegisterBusyAction(IServiceProvider services, string name, string status)
    {
        ClearSyntheticAction(services);
        _activeCts = new();
        _activeAction = services.GetRequiredService<ActionHolder>().Register(name, status, BusyMax, _activeCts);
        _activeAction.SetProgress(BusyValue);
    }

    private static void RegisterFailedAction(IServiceProvider services, string name, string error)
    {
        ClearSyntheticAction(services);
        _activeCts = new();
        _activeAction = services.GetRequiredService<ActionHolder>().Register(name, "Сбой", BusyMax, _activeCts, kind: ActionKind.Publish);
        _activeAction.Fail("Ошибка: " + error, new InvalidOperationException(error));
    }

    private static void ClearSyntheticAction(IServiceProvider services)
    {
        if (_activeAction is { } action)
        {
            action.Finish("Сброшено");
            services.GetRequiredService<ActionHolder>().Dismiss(action);
            _activeAction = null;
        }

        _activeCts?.Dispose();
        _activeCts = null;
    }
}
