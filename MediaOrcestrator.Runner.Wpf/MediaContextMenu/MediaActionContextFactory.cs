using MediaOrcestrator.Domain;
using MediaOrcestrator.Domain.Comments;
using MediaOrcestrator.Domain.Merging;
using MediaOrcestrator.Runner.Wpf.Services;
using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.Runner.Wpf.MediaContextMenu;

public sealed class MediaActionContextFactory
{
    private readonly Orcestrator _orcestrator;
    private readonly SyncRetryRunner _retry;
    private readonly BatchRenameService _rename;
    private readonly BatchPreviewService _preview;
    private readonly CoverGenerator _cover;
    private readonly MediaMergeService _merge;
    private readonly ActionHolder _actions;
    private readonly CommentsService _comments;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<MediaActionContextFactory> _logger;

    public MediaActionContextFactory(
        Orcestrator orcestrator,
        SyncRetryRunner retry,
        BatchRenameService rename,
        BatchPreviewService preview,
        CoverGenerator cover,
        MediaMergeService merge,
        ActionHolder actions,
        CommentsService comments,
        IDialogService dialogs,
        INavigationService navigation,
        ILoggerFactory loggerFactory,
        ILogger<MediaActionContextFactory> logger)
    {
        _orcestrator = orcestrator;
        _retry = retry;
        _rename = rename;
        _preview = preview;
        _cover = cover;
        _merge = merge;
        _actions = actions;
        _comments = comments;
        _dialogs = dialogs;
        _navigation = navigation;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    public MediaActionContext Create(IMediaActionUi ui)
    {
        return new(_orcestrator,
            _retry,
            _rename,
            _preview,
            _cover,
            _merge,
            _actions,
            _comments,
            _dialogs,
            _navigation,
            _loggerFactory,
            _logger,
            ui);
    }
}
