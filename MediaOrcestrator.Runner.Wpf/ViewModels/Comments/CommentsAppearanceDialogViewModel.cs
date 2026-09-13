using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace MediaOrcestrator.Runner.Wpf.ViewModels.Comments;

public sealed partial class CommentsAppearanceDialogViewModel : ObservableObject, IDialogViewModel
{
    private const string DefaultReplyPrefix = "{name}, ";
    private const double LineHeightStep = 0.05;

    [ObservableProperty]
    private int _baseFontSize;

    [ObservableProperty]
    private int _commentFontSize;

    [ObservableProperty]
    private int _authorFontSize;

    [ObservableProperty]
    private int _metaFontSize;

    [ObservableProperty]
    private int _mediaTitleFontSize;

    [ObservableProperty]
    private int _badgeFontSize;

    [ObservableProperty]
    private double _lineHeight;

    [ObservableProperty]
    private ReplyPrefixPreset? _selectedPreset;

    [ObservableProperty]
    private string _customReplyPrefix = string.Empty;

    [ObservableProperty]
    private int _selectedLayoutTabIndex;

    public CommentsAppearanceDialogViewModel(CommentsAppearance appearance, string replyPrefixTemplate, CommentsLayoutMode layoutMode)
    {
        Presets =
        [
            new("Имя, ", "{name}, "),
            new("@Имя ", "@{name} "),
            new("Имя ", "{name} "),
            new("Без префикса", ""),
            new("Свой вариант", null),
        ];

        LoadFrom(appearance);
        LoadReplyPrefix(replyPrefixTemplate);
        SelectedLayoutTabIndex = layoutMode == CommentsLayoutMode.Grouped ? 1 : 0;
    }

    public event EventHandler<bool>? RequestClose;

    public ObservableCollection<ReplyPrefixPreset> Presets { get; }

    public string ReplyPrefixTemplate => SelectedPreset?.Template ?? CustomReplyPrefix;

    public bool IsCustomReply => SelectedPreset is null || SelectedPreset.Template is null;

    public int FontSizeMin => CommentsAppearance.MinFontSize;
    public int FontSizeMax => CommentsAppearance.MaxFontSize;

    public double LineHeightMin => CommentsAppearance.MinLineHeight;
    public double LineHeightMax => CommentsAppearance.MaxLineHeight;

    public CommentsLayoutMode SelectedLayoutMode =>
        SelectedLayoutTabIndex == 1 ? CommentsLayoutMode.Grouped : CommentsLayoutMode.Flat;

    [RelayCommand]
    private void Save()
    {
        RequestClose?.Invoke(this, true);
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke(this, false);
    }

    [RelayCommand]
    private void Reset()
    {
        LoadFrom(new());
        LoadReplyPrefix(DefaultReplyPrefix);
    }

    [RelayCommand]
    private void IncreaseBaseFontSize() => BaseFontSize = Clamp(BaseFontSize + 1);

    [RelayCommand]
    private void DecreaseBaseFontSize() => BaseFontSize = Clamp(BaseFontSize - 1);

    [RelayCommand]
    private void IncreaseCommentFontSize() => CommentFontSize = Clamp(CommentFontSize + 1);

    [RelayCommand]
    private void DecreaseCommentFontSize() => CommentFontSize = Clamp(CommentFontSize - 1);

    [RelayCommand]
    private void IncreaseAuthorFontSize() => AuthorFontSize = Clamp(AuthorFontSize + 1);

    [RelayCommand]
    private void DecreaseAuthorFontSize() => AuthorFontSize = Clamp(AuthorFontSize - 1);

    [RelayCommand]
    private void IncreaseMetaFontSize() => MetaFontSize = Clamp(MetaFontSize + 1);

    [RelayCommand]
    private void DecreaseMetaFontSize() => MetaFontSize = Clamp(MetaFontSize - 1);

    [RelayCommand]
    private void IncreaseMediaTitleFontSize() => MediaTitleFontSize = Clamp(MediaTitleFontSize + 1);

    [RelayCommand]
    private void DecreaseMediaTitleFontSize() => MediaTitleFontSize = Clamp(MediaTitleFontSize - 1);

    [RelayCommand]
    private void IncreaseBadgeFontSize() => BadgeFontSize = Clamp(BadgeFontSize + 1);

    [RelayCommand]
    private void DecreaseBadgeFontSize() => BadgeFontSize = Clamp(BadgeFontSize - 1);

    [RelayCommand]
    private void IncreaseLineHeight() => LineHeight = ClampLineHeight(LineHeight + LineHeightStep);

    [RelayCommand]
    private void DecreaseLineHeight() => LineHeight = ClampLineHeight(LineHeight - LineHeightStep);

    partial void OnBaseFontSizeChanged(int value)
    {
        var clamped = Clamp(value);

        if (clamped != value)
        {
            BaseFontSize = clamped;
        }
    }

    partial void OnCommentFontSizeChanged(int value)
    {
        var clamped = Clamp(value);

        if (clamped != value)
        {
            CommentFontSize = clamped;
        }
    }

    partial void OnAuthorFontSizeChanged(int value)
    {
        var clamped = Clamp(value);

        if (clamped != value)
        {
            AuthorFontSize = clamped;
        }
    }

    partial void OnMetaFontSizeChanged(int value)
    {
        var clamped = Clamp(value);

        if (clamped != value)
        {
            MetaFontSize = clamped;
        }
    }

    partial void OnMediaTitleFontSizeChanged(int value)
    {
        var clamped = Clamp(value);

        if (clamped != value)
        {
            MediaTitleFontSize = clamped;
        }
    }

    partial void OnBadgeFontSizeChanged(int value)
    {
        var clamped = Clamp(value);

        if (clamped != value)
        {
            BadgeFontSize = clamped;
        }
    }

    partial void OnLineHeightChanged(double value)
    {
        var clamped = ClampLineHeight(value);

        if (Math.Abs(clamped - value) > 0.0001)
        {
            LineHeight = clamped;
        }
    }

    partial void OnSelectedPresetChanged(ReplyPrefixPreset? value)
    {
        OnPropertyChanged(nameof(IsCustomReply));

        if (value?.Template is not null)
        {
            CustomReplyPrefix = value.Template;
        }
    }

    partial void OnSelectedLayoutTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedLayoutMode));
    }

    public CommentsAppearance Snapshot()
    {
        var result = new CommentsAppearance
        {
            BaseFontSize = BaseFontSize,
            CommentFontSize = CommentFontSize,
            AuthorFontSize = AuthorFontSize,
            MetaFontSize = MetaFontSize,
            MediaTitleFontSize = MediaTitleFontSize,
            BadgeFontSize = BadgeFontSize,
            LineHeight = LineHeight,
        };

        result.Normalize();
        return result;
    }

    private int Clamp(int value) => Math.Clamp(value, FontSizeMin, FontSizeMax);

    private double ClampLineHeight(double value)
    {
        var clamped = Math.Clamp(value, LineHeightMin, LineHeightMax);
        return Math.Round(clamped, 2);
    }

    private void LoadFrom(CommentsAppearance appearance)
    {
        BaseFontSize = appearance.BaseFontSize;
        CommentFontSize = appearance.CommentFontSize;
        AuthorFontSize = appearance.AuthorFontSize;
        MetaFontSize = appearance.MetaFontSize;
        MediaTitleFontSize = appearance.MediaTitleFontSize;
        BadgeFontSize = appearance.BadgeFontSize;
        LineHeight = appearance.LineHeight;
    }

    private void LoadReplyPrefix(string template)
    {
        template ??= DefaultReplyPrefix;

        var preset = Presets.FirstOrDefault(p => p.Template is not null && string.Equals(p.Template, template, StringComparison.Ordinal));

        if (preset is not null)
        {
            SelectedPreset = preset;
            CustomReplyPrefix = preset.Template ?? string.Empty;
        }
        else
        {
            SelectedPreset = Presets.First(p => p.Template is null);
            CustomReplyPrefix = template;
        }
    }
}

public sealed record ReplyPrefixPreset(string Label, string? Template)
{
    public override string ToString() => Label;
}
