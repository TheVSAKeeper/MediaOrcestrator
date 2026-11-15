using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

internal static class MarkdownRenderer
{
    private static readonly Regex InlineCodeRegex = new(@"`([^`]+)`", RegexOptions.Compiled);
    private static readonly Regex BoldRegex = new(@"\*\*([^*]+)\*\*", RegexOptions.Compiled);
    private static readonly Regex ItalicRegex = new(@"(?<![*\w])\*([^*\n]+)\*(?!\*)", RegexOptions.Compiled);
    private static readonly Regex LinkRegex = new(@"\[([^\]]+)\]\(([^)]+)\)", RegexOptions.Compiled);
    private static readonly Regex AutoLinkRegex = new(@"https?://[^\s<>""']+", RegexOptions.Compiled);

    public static IEnumerable<Block> Render(string? markdown, Action<Uri> openLink)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            yield return new Paragraph(new Run(""));
            yield break;
        }

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var i = 0;

        while (i < lines.Length)
        {
            var line = lines[i];

            if (string.IsNullOrWhiteSpace(line))
            {
                i++;
                continue;
            }

            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                var codeLines = new List<string>();
                i++;

                while (i < lines.Length && !lines[i].StartsWith("```", StringComparison.Ordinal))
                {
                    codeLines.Add(lines[i]);
                    i++;
                }

                if (i < lines.Length)
                {
                    i++;
                }

                yield return BuildCodeBlock(string.Join('\n', codeLines));
                continue;
            }

            if (TryParseHeading(line, out var level, out var headingText))
            {
                yield return BuildHeading(level, headingText, openLink);
                i++;
                continue;
            }

            if (line.StartsWith("> ", StringComparison.Ordinal) || line == ">")
            {
                var quoteLines = new List<string>();

                while (i < lines.Length && (lines[i].StartsWith(">", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(lines[i])))
                {
                    if (string.IsNullOrWhiteSpace(lines[i]) && quoteLines.Count > 0)
                    {
                        break;
                    }

                    quoteLines.Add(lines[i].TrimStart('>').TrimStart());
                    i++;
                }

                yield return BuildQuote(string.Join(' ', quoteLines), openLink);
                continue;
            }

            if (IsListItem(line, out _, out _))
            {
                var listItems = new List<string>();
                var ordered = char.IsDigit(line.TrimStart()[0]);

                while (i < lines.Length && IsListItem(lines[i], out _, out var itemText))
                {
                    listItems.Add(itemText);
                    i++;
                }

                yield return BuildList(listItems, ordered, openLink);
                continue;
            }

            var paragraph = new List<string> { line };
            i++;

            while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i])
                                     && !lines[i].StartsWith('#')
                                     && !lines[i].StartsWith("```", StringComparison.Ordinal)
                                     && !lines[i].StartsWith('>')
                                     && !IsListItem(lines[i], out _, out _))
            {
                paragraph.Add(lines[i]);
                i++;
            }

            yield return BuildParagraph(string.Join(' ', paragraph), openLink);
        }
    }

    private static bool TryParseHeading(string line, out int level, out string text)
    {
        level = 0;
        text = string.Empty;

        if (!line.StartsWith('#'))
        {
            return false;
        }

        while (level < line.Length && line[level] == '#')
        {
            level++;
        }

        if (level is < 1 or > 6 || level >= line.Length || line[level] != ' ')
        {
            return false;
        }

        text = line[(level + 1)..].Trim();
        return true;
    }

    private static bool IsListItem(string line, out string marker, out string text)
    {
        marker = string.Empty;
        text = string.Empty;
        var trimmed = line.TrimStart();

        if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
        {
            marker = trimmed[..1];
            text = trimmed[2..];
            return true;
        }

        var dotIndex = trimmed.IndexOf('.');

        if (dotIndex > 0 && dotIndex < 4 && int.TryParse(trimmed.AsSpan(0, dotIndex), out _)
            && dotIndex + 1 < trimmed.Length && trimmed[dotIndex + 1] == ' ')
        {
            marker = trimmed[..(dotIndex + 1)];
            text = trimmed[(dotIndex + 2)..];
            return true;
        }

        return false;
    }

    private static Block BuildHeading(int level, string text, Action<Uri> openLink)
    {
        var paragraph = new Paragraph
        {
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, level == 1 ? 16 : 12, 0, 6),
        };

        paragraph.SetResourceReference(TextElement.ForegroundProperty, "Fg.Primary");

        paragraph.FontSize = level switch
        {
            1 => 22,
            2 => 18,
            3 => 16,
            _ => 14,
        };

        foreach (var inline in BuildInlines(text, openLink))
        {
            paragraph.Inlines.Add(inline);
        }

        return paragraph;
    }

    private static Block BuildParagraph(string text, Action<Uri> openLink)
    {
        var paragraph = new Paragraph
        {
            Margin = new Thickness(0, 0, 0, 8),
        };

        foreach (var inline in BuildInlines(text, openLink))
        {
            paragraph.Inlines.Add(inline);
        }

        return paragraph;
    }

    private static Block BuildQuote(string text, Action<Uri> openLink)
    {
        var paragraph = new Paragraph
        {
            Margin = new Thickness(12, 4, 0, 8),
            Padding = new Thickness(10, 6, 10, 6),
            BorderThickness = new Thickness(3, 0, 0, 0),
        };

        paragraph.SetResourceReference(TextElement.ForegroundProperty, "Fg.Secondary");
        paragraph.SetResourceReference(Block.BorderBrushProperty, "Accent.Primary");
        paragraph.SetResourceReference(TextElement.BackgroundProperty, "Bg.SurfaceAlt");

        foreach (var inline in BuildInlines(text, openLink))
        {
            paragraph.Inlines.Add(inline);
        }

        return paragraph;
    }

    private static Block BuildCodeBlock(string code)
    {
        var paragraph = new Paragraph
        {
            FontSize = 12,
            Margin = new Thickness(0, 4, 0, 10),
            Padding = new Thickness(10, 8, 10, 8),
        };

        paragraph.SetResourceReference(TextElement.FontFamilyProperty, "Font.Mono");
        paragraph.SetResourceReference(TextElement.BackgroundProperty, "Bg.SurfaceAlt");
        paragraph.SetResourceReference(TextElement.ForegroundProperty, "Fg.Primary");
        paragraph.Inlines.Add(new Run(code));
        return paragraph;
    }

    private static Block BuildList(List<string> items, bool ordered, Action<Uri> openLink)
    {
        var list = new List
        {
            MarkerStyle = ordered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
            Padding = new Thickness(0),
            Margin = new Thickness(20, 0, 0, 8),
        };

        foreach (var item in items)
        {
            var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 2) };

            foreach (var inline in BuildInlines(item, openLink))
            {
                paragraph.Inlines.Add(inline);
            }

            list.ListItems.Add(new ListItem(paragraph));
        }

        return list;
    }

    private static IEnumerable<Inline> BuildInlines(string text, Action<Uri> openLink)
    {
        var tokens = new List<(int Start, int Length, Inline Inline)>();

        foreach (Match m in InlineCodeRegex.Matches(text))
        {
            var run = new Run(m.Groups[1].Value);
            run.SetResourceReference(TextElement.FontFamilyProperty, "Font.Mono");
            run.SetResourceReference(TextElement.BackgroundProperty, "Bg.SurfaceAlt");
            tokens.Add((m.Index, m.Length, run));
        }

        foreach (Match m in LinkRegex.Matches(text))
        {
            if (Overlaps(tokens, m.Index, m.Length))
            {
                continue;
            }

            tokens.Add((m.Index, m.Length, BuildLink(m.Groups[1].Value, m.Groups[2].Value, openLink)));
        }

        foreach (Match m in AutoLinkRegex.Matches(text))
        {
            if (Overlaps(tokens, m.Index, m.Length))
            {
                continue;
            }

            tokens.Add((m.Index, m.Length, BuildLink(m.Value, m.Value, openLink)));
        }

        foreach (Match m in BoldRegex.Matches(text))
        {
            if (Overlaps(tokens, m.Index, m.Length))
            {
                continue;
            }

            tokens.Add((m.Index, m.Length, new Bold(new Run(m.Groups[1].Value))));
        }

        foreach (Match m in ItalicRegex.Matches(text))
        {
            if (Overlaps(tokens, m.Index, m.Length))
            {
                continue;
            }

            tokens.Add((m.Index, m.Length, new Italic(new Run(m.Groups[1].Value))));
        }

        tokens.Sort((a, b) => a.Start.CompareTo(b.Start));

        var pos = 0;

        foreach (var (start, length, inline) in tokens)
        {
            if (start < pos)
            {
                continue;
            }

            if (start > pos)
            {
                yield return new Run(text.Substring(pos, start - pos));
            }

            yield return inline;
            pos = start + length;
        }

        if (pos < text.Length)
        {
            yield return new Run(text[pos..]);
        }
    }

    private static bool Overlaps(List<(int Start, int Length, Inline Inline)> tokens, int start, int length)
    {
        var end = start + length;

        foreach (var (s, l, _) in tokens)
        {
            var te = s + l;

            if (start < te && s < end)
            {
                return true;
            }
        }

        return false;
    }

    private static Hyperlink BuildLink(string text, string url, Action<Uri> openLink)
    {
        var link = new Hyperlink(new Run(text));

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            link.NavigateUri = uri;
            link.RequestNavigate += (_, e) => OnNavigate(e, openLink);
            link.Cursor = Cursors.Hand;
        }

        link.SetResourceReference(TextElement.ForegroundProperty, "Accent.Primary");
        return link;
    }

    private static void OnNavigate(System.Windows.Navigation.RequestNavigateEventArgs e, Action<Uri> openLink)
    {
        openLink(e.Uri);
        e.Handled = true;
    }
}
