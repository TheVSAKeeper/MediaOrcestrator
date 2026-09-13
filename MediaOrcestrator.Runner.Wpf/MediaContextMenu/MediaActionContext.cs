using MediaOrcestrator.Domain;
using MediaOrcestrator.Domain.Comments;
using MediaOrcestrator.Domain.Merging;
using MediaOrcestrator.Runner.Wpf.Services;
using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Runner.Wpf.MediaContextMenu;

public sealed record MediaActionContext(
    Orcestrator Orcestrator,
    SyncRetryRunner RetryRunner,
    BatchRenameService BatchRenameService,
    BatchPreviewService BatchPreviewService,
    CoverGenerator CoverGenerator,
    MediaMergeService MergeService,
    ActionHolder ActionHolder,
    CommentsService CommentsService,
    IDialogService Dialogs,
    INavigationService Navigation,
    ILoggerFactory LoggerFactory,
    ILogger Logger,
    IMediaActionUi Ui);
