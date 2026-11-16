using CommunityToolkit.Mvvm.Input;

namespace MediaOrcestrator.Runner.Wpf.ViewModels.Comments;

public sealed partial class CommentItemViewModel
{
    public void ApplyLikeUpdate(bool likedByMe, int likeCount)
    {
        LikedByMe = likedByMe;
        LikeCount = likeCount;
    }

    public void ApplyDeletedUpdate(bool isDeleted)
    {
        IsDeleted = isDeleted;
    }

    [RelayCommand]
    private async Task ToggleLikeAsync()
    {
        if (!HasLikes || IsLikeBusy)
        {
            return;
        }

        var nextLiked = !LikedByMe;
        IsLikeBusy = true;
        ErrorMessage = null;

        try
        {
            await _host.ExecuteLikeAsync(this, nextLiked);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLikeBusy = false;
        }
    }

    [RelayCommand]
    private void StartReply()
    {
        _host.StartReply(this);
    }

    [RelayCommand]
    private void StartEdit()
    {
        if (!HasMutations || !CanEdit || IsDeleted)
        {
            return;
        }

        ErrorMessage = null;
        EditText = DisplayText;
        IsEditing = true;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
        EditText = string.Empty;
        ErrorMessage = null;
    }

    [RelayCommand]
    private async Task SaveEditAsync()
    {
        if (!IsEditing || IsMutationBusy)
        {
            return;
        }

        var newText = (EditText ?? string.Empty).Trim();

        if (newText.Length == 0)
        {
            ErrorMessage = "Текст не должен быть пустым.";
            return;
        }

        if (string.Equals(newText, DisplayText, StringComparison.Ordinal))
        {
            IsEditing = false;
            return;
        }

        IsMutationBusy = true;
        ErrorMessage = null;

        try
        {
            var updated = await _host.ExecuteEditAsync(this, newText);

            if (updated is not null)
            {
                DisplayText = updated;
            }

            IsEditing = false;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsMutationBusy = false;
        }
    }

    [RelayCommand]
    private void RequestDelete()
    {
        if (!HasMutations || !CanDelete || IsDeleted)
        {
            return;
        }

        ErrorMessage = null;
        IsConfirmingDelete = true;
        StartDeleteAutoCancel();
    }

    [RelayCommand]
    private async Task ConfirmDeleteAsync()
    {
        if (!IsConfirmingDelete || IsMutationBusy)
        {
            return;
        }

        StopDeleteAutoCancel();
        IsMutationBusy = true;

        try
        {
            await _host.ExecuteDeleteAsync(this);
            IsDeleted = true;
            IsConfirmingDelete = false;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsMutationBusy = false;
        }
    }

    [RelayCommand]
    private void CancelDelete()
    {
        StopDeleteAutoCancel();
        IsConfirmingDelete = false;
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        if (!HasMutations || !IsDeleted || IsMutationBusy)
        {
            return;
        }

        IsMutationBusy = true;
        ErrorMessage = null;

        try
        {
            await _host.ExecuteRestoreAsync(this);
            IsDeleted = false;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsMutationBusy = false;
        }
    }

    [RelayCommand]
    private void OpenInSource()
    {
        if (HasCommentPermalink)
        {
            _host.OpenCommentInSource(this);
        }
    }

    [RelayCommand]
    private void OpenMediaInSource()
    {
        if (HasMediaExternalLink)
        {
            _host.OpenMediaInSource(this);
        }
    }

    private void StartDeleteAutoCancel()
    {
        _deleteConfirmTimer ??= _host.Dispatcher.CreateTimer(
            TimeSpan.FromMilliseconds(DeleteConfirmTimeoutMs),
            OnDeleteConfirmTimeout);

        _deleteConfirmTimer.Stop();
        _deleteConfirmTimer.Start();
    }

    private void OnDeleteConfirmTimeout()
    {
        StopDeleteAutoCancel();
        IsConfirmingDelete = false;
    }

    private void StopDeleteAutoCancel()
    {
        _deleteConfirmTimer?.Stop();
    }
}
