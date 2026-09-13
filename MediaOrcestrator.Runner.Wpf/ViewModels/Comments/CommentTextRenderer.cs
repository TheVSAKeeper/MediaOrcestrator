using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace MediaOrcestrator.Runner.Wpf.ViewModels.Comments;

internal static class CommentTextRenderer
{
    private static readonly Regex TokenRegex = new(
        """\[(id|club|public)(\d+)\|([^\]]+)\]|@@?([A-Za-z][A-Za-z0-9_.\-]*|[А-Яа-яЁё][А-Яа-яЁё0-9_.\-]*)|https?:\/\/[^\s<>"']+""",
        RegexOptions.Compiled);

    private static readonly Regex TrailingPunctuation = new("""[)\].,!?;:]+$""", RegexOptions.Compiled);

    private static readonly Regex YoutubeHandleRegex = new("^[A-Za-z][A-Za-z0-9_.\\-]*$", RegexOptions.Compiled);

    public static IEnumerable<Inline> BuildInlines(string? text, string? search, ICommentItemHost host)
    {
        if (string.IsNullOrEmpty(text))
        {
            yield break;
        }

        var search0 = string.IsNullOrWhiteSpace(search) ? null : search;

        var lastEnd = 0;

        foreach (Match match in TokenRegex.Matches(text))
        {
            if (match.Index > lastEnd)
            {
                foreach (var inline in BuildHighlightedRuns(text.AsSpan(lastEnd, match.Index - lastEnd).ToString(), search0))
                {
                    yield return inline;
                }
            }

            if (match.Groups[1].Success)
            {
                var vkType = match.Groups[1].Value;
                var vkId = match.Groups[2].Value;
                var name = match.Groups[3].Value;
                var url = vkType == "id" ? $"https://vk.com/id{vkId}" : $"https://vk.com/{vkType}{vkId}";

                yield return BuildMention(name, url, host);
            }
            else if (match.Groups[4].Success)
            {
                var handle = match.Groups[4].Value;
                var url = YoutubeHandleRegex.IsMatch(handle) ? $"https://www.youtube.com/@{handle}" : null;

                yield return BuildMention("@" + handle, url, host);
            }
            else
            {
                var raw = match.Value;
                var trailingMatch = TrailingPunctuation.Match(raw);
                var trail = trailingMatch.Success ? trailingMatch.Value : string.Empty;
                var bare = trailingMatch.Success ? raw[..^trail.Length] : raw;

                yield return BuildExternalLink(bare, bare, host);

                if (trail.Length > 0)
                {
                    foreach (var inline in BuildHighlightedRuns(trail, search0))
                    {
                        yield return inline;
                    }
                }
            }

            lastEnd = match.Index + match.Length;

            if (match.Length == 0)
            {
                lastEnd++;
            }
        }

        if (lastEnd < text.Length)
        {
            foreach (var inline in BuildHighlightedRuns(text[lastEnd..], search0))
            {
                yield return inline;
            }
        }
    }

    private static IEnumerable<Inline> BuildHighlightedRuns(string text, string? search)
    {
        if (string.IsNullOrEmpty(text))
        {
            yield break;
        }

        if (string.IsNullOrEmpty(search))
        {
            yield return new Run(text);
            yield break;
        }

        var idx = 0;

        while (idx < text.Length)
        {
            var matchIdx = text.IndexOf(search, idx, StringComparison.CurrentCultureIgnoreCase);

            if (matchIdx < 0)
            {
                yield return new Run(text[idx..]);
                yield break;
            }

            if (matchIdx > idx)
            {
                yield return new Run(text[idx..matchIdx]);
            }

            var matched = text.Substring(matchIdx, search.Length);
            var highlight = new Run(matched);
            highlight.SetResourceReference(TextElement.BackgroundProperty, "State.WarningSoft");
            yield return highlight;

            idx = matchIdx + search.Length;
        }
    }

    private static Inline BuildMention(string visibleText, string? url, ICommentItemHost host)
    {
        if (string.IsNullOrEmpty(url))
        {
            var styled = new Run(visibleText)
            {
                FontWeight = FontWeights.SemiBold,
            };

            styled.SetResourceReference(TextElement.ForegroundProperty, "Accent.Primary");
            return styled;
        }

        var hyperlink = new Hyperlink(new Run(visibleText))
        {
            FontWeight = FontWeights.SemiBold,
        };

        try
        {
            hyperlink.NavigateUri = new(url);
        }
        catch (UriFormatException)
        {
            return new Run(visibleText);
        }

        hyperlink.SetResourceReference(TextElement.ForegroundProperty, "Accent.Primary");
        hyperlink.RequestNavigate += (_, e) => OnRequestNavigate(e, host);
        return hyperlink;
    }

    private static Inline BuildExternalLink(string visibleText, string url, ICommentItemHost host)
    {
        var hyperlink = new Hyperlink(new Run(visibleText));

        try
        {
            hyperlink.NavigateUri = new(url);
        }
        catch (UriFormatException)
        {
            return new Run(visibleText);
        }

        hyperlink.SetResourceReference(TextElement.ForegroundProperty, "Accent.Primary");
        hyperlink.RequestNavigate += (_, e) => OnRequestNavigate(e, host);
        return hyperlink;
    }

    private static void OnRequestNavigate(System.Windows.Navigation.RequestNavigateEventArgs e, ICommentItemHost host)
    {
        host.OpenExternalLink(e.Uri);
        e.Handled = true;
    }
}
