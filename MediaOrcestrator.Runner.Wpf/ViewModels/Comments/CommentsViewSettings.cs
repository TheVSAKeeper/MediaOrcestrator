using MediaOrcestrator.Runner.Wpf.Bootstrap;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace MediaOrcestrator.Runner.Wpf.ViewModels.Comments;

public enum CommentsLayoutMode
{
    None = 0,
    Flat = 1,
    Grouped = 2,
}

public enum CommentsReplyStatusFilter
{
    None = 0,
    All = 1,
    WithoutReply = 2,
    WithReply = 3,
    NewReplies = 4,
    WithoutReplyAndLike = 5,
}

public sealed class CommentsAppearance
{
    public const int MinFontSize = 8;
    public const int MaxFontSize = 32;
    public const double MinLineHeight = 1.0;
    public const double MaxLineHeight = 2.5;

    public int BaseFontSize { get; set; } = 13;
    public int CommentFontSize { get; set; } = 13;
    public int AuthorFontSize { get; set; } = 12;
    public int MetaFontSize { get; set; } = 12;
    public int MediaTitleFontSize { get; set; } = 14;
    public int BadgeFontSize { get; set; } = 10;
    public double LineHeight { get; set; } = 1.45;

    public void Normalize()
    {
        BaseFontSize = Math.Clamp(BaseFontSize, MinFontSize, MaxFontSize);
        CommentFontSize = Math.Clamp(CommentFontSize, MinFontSize, MaxFontSize);
        AuthorFontSize = Math.Clamp(AuthorFontSize, MinFontSize, MaxFontSize);
        MetaFontSize = Math.Clamp(MetaFontSize, MinFontSize, MaxFontSize);
        MediaTitleFontSize = Math.Clamp(MediaTitleFontSize, MinFontSize, MaxFontSize);
        BadgeFontSize = Math.Clamp(BadgeFontSize, MinFontSize, MaxFontSize);
        LineHeight = Math.Clamp(LineHeight, MinLineHeight, MaxLineHeight);
    }

    public CommentsAppearance Clone()
    {
        return new()
        {
            BaseFontSize = BaseFontSize,
            CommentFontSize = CommentFontSize,
            AuthorFontSize = AuthorFontSize,
            MetaFontSize = MetaFontSize,
            MediaTitleFontSize = MediaTitleFontSize,
            BadgeFontSize = BadgeFontSize,
            LineHeight = LineHeight,
        };
    }
}

public sealed class CommentsViewSettings
{
    public const int DefaultFetchMaxParallelism = 4;
    public const int MinFetchMaxParallelism = 1;
    public const int MaxFetchMaxParallelism = 64;

    private const string LegacyFileName = "wpf-comments-view-settings.json";
    private const string LegacyBackupFileName = "wpf-comments-view-settings.json.bak";

    private const string KeySelectedSources = "wpf.comments.view.selected_sources";
    private const string KeyLimit = "wpf.comments.view.limit";
    private const string KeySearch = "wpf.comments.view.search";
    private const string KeyFetchSinceDays = "wpf.comments.view.fetch_since_days";
    private const string KeyFetchOnlyRecent = "wpf.comments.view.fetch_only_recent";
    private const string KeyFetchMaxParallelism = "wpf.comments.view.fetch_max_parallelism";
    private const string KeyLayoutMode = "wpf.comments.view.layout_mode";
    private const string KeyReplyStatus = "wpf.comments.view.reply_status";
    private const string KeyReplyPrefix = "wpf.comments.view.reply_prefix";

    private const string KeyAppearanceBase = "wpf.comments.view.appearance.base_size";
    private const string KeyAppearanceComment = "wpf.comments.view.appearance.comment_size";
    private const string KeyAppearanceAuthor = "wpf.comments.view.appearance.author_size";
    private const string KeyAppearanceMeta = "wpf.comments.view.appearance.meta_size";
    private const string KeyAppearanceMediaTitle = "wpf.comments.view.appearance.media_title_size";
    private const string KeyAppearanceBadge = "wpf.comments.view.appearance.badge_size";
    private const string KeyAppearanceLineHeight = "wpf.comments.view.appearance.line_height";

    public List<string> SelectedSourceIds { get; set; } = [];
    public int Limit { get; set; } = 1000;
    public string Search { get; set; } = "";
    public int FetchSinceDays { get; set; }
    public int FetchOnlyRecent { get; set; }
    public int FetchMaxParallelism { get; set; } = DefaultFetchMaxParallelism;
    public CommentsLayoutMode LayoutMode { get; set; } = CommentsLayoutMode.Flat;
    public CommentsReplyStatusFilter ReplyStatus { get; set; } = CommentsReplyStatusFilter.WithoutReplyAndLike;
    public CommentsAppearance Appearance { get; set; } = new();
    public string ReplyPrefixTemplate { get; set; } = "{name}, ";

    public static CommentsViewSettings Load(ISettingsStore store)
    {
        TryMigrateLegacyJson(store);

        var s = new CommentsViewSettings();

        if (store.GetStringValue(KeySelectedSources) is { Length: > 0 } sources)
        {
            s.SelectedSourceIds = sources.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        s.Limit = ReadInt(store, KeyLimit, 1000);
        s.Search = store.GetStringValue(KeySearch) ?? string.Empty;
        s.FetchSinceDays = ReadInt(store, KeyFetchSinceDays, 0);
        s.FetchOnlyRecent = ReadInt(store, KeyFetchOnlyRecent, 0);
        s.FetchMaxParallelism = ReadInt(store, KeyFetchMaxParallelism, DefaultFetchMaxParallelism);
        s.LayoutMode = ReadEnum(store, KeyLayoutMode, CommentsLayoutMode.Flat);
        s.ReplyStatus = ReadEnum(store, KeyReplyStatus, CommentsReplyStatusFilter.WithoutReplyAndLike);
        s.ReplyPrefixTemplate = store.GetStringValue(KeyReplyPrefix) ?? "{name}, ";

        s.Appearance.BaseFontSize = ReadInt(store, KeyAppearanceBase, s.Appearance.BaseFontSize);
        s.Appearance.CommentFontSize = ReadInt(store, KeyAppearanceComment, s.Appearance.CommentFontSize);
        s.Appearance.AuthorFontSize = ReadInt(store, KeyAppearanceAuthor, s.Appearance.AuthorFontSize);
        s.Appearance.MetaFontSize = ReadInt(store, KeyAppearanceMeta, s.Appearance.MetaFontSize);
        s.Appearance.MediaTitleFontSize = ReadInt(store, KeyAppearanceMediaTitle, s.Appearance.MediaTitleFontSize);
        s.Appearance.BadgeFontSize = ReadInt(store, KeyAppearanceBadge, s.Appearance.BadgeFontSize);
        s.Appearance.LineHeight = ReadDouble(store, KeyAppearanceLineHeight, s.Appearance.LineHeight);

        s.Normalize();
        return s;
    }

    public void Save(ISettingsStore store)
    {
        Normalize();

        store.SetValue(KeySelectedSources, string.Join(';', SelectedSourceIds));
        store.SetValue(KeyLimit, Limit.ToString(CultureInfo.InvariantCulture));
        store.SetValue(KeySearch, Search ?? string.Empty);
        store.SetValue(KeyFetchSinceDays, FetchSinceDays.ToString(CultureInfo.InvariantCulture));
        store.SetValue(KeyFetchOnlyRecent, FetchOnlyRecent.ToString(CultureInfo.InvariantCulture));
        store.SetValue(KeyFetchMaxParallelism, FetchMaxParallelism.ToString(CultureInfo.InvariantCulture));
        store.SetValue(KeyLayoutMode, LayoutMode.ToString());
        store.SetValue(KeyReplyStatus, ReplyStatus.ToString());
        store.SetValue(KeyReplyPrefix, ReplyPrefixTemplate ?? "{name}, ");

        store.SetValue(KeyAppearanceBase, Appearance.BaseFontSize.ToString(CultureInfo.InvariantCulture));
        store.SetValue(KeyAppearanceComment, Appearance.CommentFontSize.ToString(CultureInfo.InvariantCulture));
        store.SetValue(KeyAppearanceAuthor, Appearance.AuthorFontSize.ToString(CultureInfo.InvariantCulture));
        store.SetValue(KeyAppearanceMeta, Appearance.MetaFontSize.ToString(CultureInfo.InvariantCulture));
        store.SetValue(KeyAppearanceMediaTitle, Appearance.MediaTitleFontSize.ToString(CultureInfo.InvariantCulture));
        store.SetValue(KeyAppearanceBadge, Appearance.BadgeFontSize.ToString(CultureInfo.InvariantCulture));
        store.SetValue(KeyAppearanceLineHeight, Appearance.LineHeight.ToString("0.##", CultureInfo.InvariantCulture));
    }

    private void Normalize()
    {
        Appearance ??= new();
        Appearance.Normalize();

        FetchMaxParallelism = FetchMaxParallelism <= 0
            ? DefaultFetchMaxParallelism
            : Math.Clamp(FetchMaxParallelism, MinFetchMaxParallelism, MaxFetchMaxParallelism);

        Limit = Math.Clamp(Limit <= 0 ? 1000 : Limit, 100, 10000);
        ReplyPrefixTemplate ??= "{name}, ";
        SelectedSourceIds ??= [];
    }

    private static int ReadInt(ISettingsStore store, string key, int fallback)
    {
        var raw = store.GetStringValue(key);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }

    private static double ReadDouble(ISettingsStore store, string key, double fallback)
    {
        var raw = store.GetStringValue(key);
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }

    private static TEnum ReadEnum<TEnum>(ISettingsStore store, string key, TEnum fallback) where TEnum : struct, Enum
    {
        var raw = store.GetStringValue(key);
        return Enum.TryParse<TEnum>(raw, ignoreCase: true, out var v) ? v : fallback;
    }

    private static void TryMigrateLegacyJson(ISettingsStore store)
    {
        if (store.GetStringValue(KeyLimit) is not null || store.GetStringValue(KeyLayoutMode) is not null)
        {
            return;
        }

        var path = Path.Combine(AppContext.BaseDirectory, LegacyFileName);

        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            var s = new CommentsViewSettings();

            if (TryGetArray(root, "SelectedSourceIds", out var arr))
            {
                s.SelectedSourceIds = arr.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString() ?? string.Empty)
                    .Where(v => !string.IsNullOrEmpty(v))
                    .ToList();
            }

            s.Limit = TryGetInt(root, "Limit") ?? s.Limit;
            s.Search = TryGetString(root, "Search") ?? s.Search;
            s.FetchSinceDays = TryGetInt(root, "FetchSinceDays") ?? s.FetchSinceDays;
            s.FetchOnlyRecent = TryGetInt(root, "FetchOnlyRecent") ?? s.FetchOnlyRecent;
            s.FetchMaxParallelism = TryGetInt(root, "FetchMaxParallelism") ?? s.FetchMaxParallelism;
            s.ReplyPrefixTemplate = TryGetString(root, "ReplyPrefixTemplate") ?? s.ReplyPrefixTemplate;

            if (TryGetString(root, "LayoutMode") is { } layoutRaw
                && Enum.TryParse<CommentsLayoutMode>(layoutRaw, ignoreCase: true, out var layoutMode))
            {
                s.LayoutMode = layoutMode;
            }

            if (TryGetString(root, "ReplyStatus") is { } replyRaw
                && Enum.TryParse<CommentsReplyStatusFilter>(replyRaw, ignoreCase: true, out var replyStatus))
            {
                s.ReplyStatus = replyStatus;
            }

            if (root.TryGetProperty("Appearance", out var appearance) && appearance.ValueKind == JsonValueKind.Object)
            {
                s.Appearance.BaseFontSize = TryGetInt(appearance, "BaseFontSize") ?? s.Appearance.BaseFontSize;
                s.Appearance.CommentFontSize = TryGetInt(appearance, "CommentFontSize") ?? s.Appearance.CommentFontSize;
                s.Appearance.AuthorFontSize = TryGetInt(appearance, "AuthorFontSize") ?? s.Appearance.AuthorFontSize;
                s.Appearance.MetaFontSize = TryGetInt(appearance, "MetaFontSize") ?? s.Appearance.MetaFontSize;
                s.Appearance.MediaTitleFontSize = TryGetInt(appearance, "MediaTitleFontSize") ?? s.Appearance.MediaTitleFontSize;
                s.Appearance.BadgeFontSize = TryGetInt(appearance, "BadgeFontSize") ?? s.Appearance.BadgeFontSize;
                s.Appearance.LineHeight = TryGetDouble(appearance, "LineHeight") ?? s.Appearance.LineHeight;
            }

            s.Save(store);

            var backup = Path.Combine(AppContext.BaseDirectory, LegacyBackupFileName);

            try
            {
                if (File.Exists(backup))
                {
                    File.Delete(backup);
                }

                File.Move(path, backup);
            }
            catch
            {
            }
        }
        catch
        {
        }
    }

    private static bool TryGetArray(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        value = default;
        return false;
    }

    private static string? TryGetString(JsonElement obj, string name)
    {
        return obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    private static int? TryGetInt(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v))
        {
            return null;
        }

        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt32(out var i) => i,
            JsonValueKind.String when int.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => i,
            _ => null,
        };
    }

    private static double? TryGetDouble(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v))
        {
            return null;
        }

        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetDouble(out var d) => d,
            JsonValueKind.String when double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            _ => null,
        };
    }
}
