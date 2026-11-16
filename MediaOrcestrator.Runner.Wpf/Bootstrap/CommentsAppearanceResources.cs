using MediaOrcestrator.Runner.Wpf.ViewModels.Comments;
using System.Windows;

namespace MediaOrcestrator.Runner.Wpf.Bootstrap;

public static class CommentsAppearanceResources
{
    public const string KeyBase = "Comments.BaseFontSize";
    public const string KeyComment = "Comments.CommentFontSize";
    public const string KeyAuthor = "Comments.AuthorFontSize";
    public const string KeyMeta = "Comments.MetaFontSize";
    public const string KeyMediaTitle = "Comments.MediaTitleFontSize";
    public const string KeyBadge = "Comments.BadgeFontSize";
    public const string KeyLineHeight = "Comments.LineHeight";

    public static void Apply(CommentsAppearance appearance)
    {
        var app = Application.Current;

        if (app is null)
        {
            return;
        }

        appearance.Normalize();

        app.Resources[KeyBase] = (double)appearance.BaseFontSize;
        app.Resources[KeyComment] = (double)appearance.CommentFontSize;
        app.Resources[KeyAuthor] = (double)appearance.AuthorFontSize;
        app.Resources[KeyMeta] = (double)appearance.MetaFontSize;
        app.Resources[KeyMediaTitle] = (double)appearance.MediaTitleFontSize;
        app.Resources[KeyBadge] = (double)appearance.BadgeFontSize;
        app.Resources[KeyLineHeight] = appearance.LineHeight;
    }

    public static void ApplyDefaultsIfMissing()
    {
        var app = Application.Current;

        if (app is null || app.Resources.Contains(KeyBase))
        {
            return;
        }

        Apply(new());
    }
}
