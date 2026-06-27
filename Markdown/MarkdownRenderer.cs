using System.Text;
using System.Text.RegularExpressions;

namespace ClassicBlog.Markdown;

/// <summary>
/// Best-effort, safe-by-construction Markdown → HTML converter.
/// Supports a useful subset: headings, paragraphs, line breaks, blockquotes,
/// ordered/unordered lists, fenced code blocks, horizontal rules, inline code,
/// images, links, bold, italic, and strikethrough.
/// All text is HTML-escaped; only whitelisted constructs are emitted, and link
/// protocols are filtered, so user-submitted Markdown (e.g. comments) cannot
/// inject raw HTML or script.
/// </summary>
public static class MarkdownRenderer
{
    private static readonly Regex CodeFence = new(@"^\s*(`{3,})(.*)$", RegexOptions.Compiled);
    private static readonly Regex Heading = new(@"^\s*(#{1,6})\s+(.+?)\s*#*\s*$", RegexOptions.Compiled);
    private static readonly Regex Hr = new(@"^\s*([-*_])(\s*\1){2,}\s*$", RegexOptions.Compiled);
    private static readonly Regex UnorderedItem = new(@"^\s*[-*+]\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex OrderedItem = new(@"^\s*\d+\.\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex Blockquote = new(@"^\s*>\s?(.*)$", RegexOptions.Compiled);

    private static readonly Regex InlineCode = new(@"`([^`]+)`", RegexOptions.Compiled);
    private static readonly Regex Image = new(@"!\[([^\]]*)\]\(([^\s)]+)\)", RegexOptions.Compiled);
    private static readonly Regex Link = new(@"\[([^\]]+)\]\(([^\s)]+)(?:\s+""[^""]*"")?\)", RegexOptions.Compiled);
    private static readonly Regex Bold = new(@"\*\*(.+?)\*\*|__(.+?)__", RegexOptions.Compiled);
    // Italic via single `*` only. Underscore-italics are deliberately omitted so
    // that snake_case identifiers in text are not mangled.
    private static readonly Regex Italic = new(@"\*([^*\s][^*]*?)\*", RegexOptions.Compiled);
    private static readonly Regex Strike = new(@"~~(.+?)~~", RegexOptions.Compiled);
    private static readonly Regex CodePlaceholder = new(@"@@CODE(\d+)@@", RegexOptions.Compiled);

    public static string ToHtml(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return string.Empty;

        var normalized = markdown.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var sb = new StringBuilder();
        var i = 0;

        while (i < lines.Length)
        {
            var line = lines[i];

            // Fenced code block
            if (CodeFence.Match(line).Success)
            {
                i++;
                var code = new StringBuilder();
                while (i < lines.Length && !CodeFence.Match(lines[i]).Success)
                {
                    code.AppendLine(lines[i]);
                    i++;
                }
                if (i < lines.Length) i++; // consume closing fence
                sb.Append("<pre><code>").Append(Escape(code.ToString().TrimEnd('\n'))).Append("</code></pre>\n");
                continue;
            }

            if (string.IsNullOrWhiteSpace(line)) { i++; continue; }

            // Heading
            var h = Heading.Match(line);
            if (h.Success)
            {
                var level = Math.Min(h.Groups[1].Length, 6);
                sb.Append($"<h{level}>").Append(Inline(h.Groups[2].Value)).Append($"</h{level}>\n");
                i++; continue;
            }

            // Horizontal rule
            if (Hr.IsMatch(line)) { sb.Append("<hr/>\n"); i++; continue; }

            // Blockquote
            if (Blockquote.IsMatch(line))
            {
                var quote = new StringBuilder();
                while (i < lines.Length && Blockquote.Match(lines[i]).Success)
                {
                    quote.AppendLine(Blockquote.Match(lines[i]).Groups[1].Value);
                    i++;
                }
                // Recurse so nested Markdown inside the quote is rendered.
                sb.Append("<blockquote>").Append(ToHtml(quote.ToString().TrimEnd('\n'))).Append("</blockquote>\n");
                continue;
            }

            // Unordered list
            if (UnorderedItem.IsMatch(line))
            {
                sb.Append("<ul>\n");
                while (i < lines.Length && UnorderedItem.IsMatch(lines[i]))
                {
                    sb.Append("<li>").Append(Inline(UnorderedItem.Match(lines[i]).Groups[1].Value)).Append("</li>\n");
                    i++;
                }
                sb.Append("</ul>\n");
                continue;
            }

            // Ordered list
            if (OrderedItem.IsMatch(line))
            {
                sb.Append("<ol>\n");
                while (i < lines.Length && OrderedItem.IsMatch(lines[i]))
                {
                    sb.Append("<li>").Append(Inline(OrderedItem.Match(lines[i]).Groups[1].Value)).Append("</li>\n");
                    i++;
                }
                sb.Append("</ol>\n");
                continue;
            }

            // Paragraph: gather consecutive non-blank, non-block-starter lines.
            // Single newlines become <br/> (GitHub-style soft breaks).
            var para = new StringBuilder();
            while (i < lines.Length &&
                   !string.IsNullOrWhiteSpace(lines[i]) &&
                   !IsBlockStart(lines[i]))
            {
                if (para.Length > 0) para.Append("<br/>\n");
                para.Append(lines[i]);
                i++;
            }
            sb.Append("<p>").Append(Inline(para.ToString())).Append("</p>\n");
        }

        return sb.ToString();
    }

    private static bool IsBlockStart(string line) =>
        CodeFence.IsMatch(line) || Heading.IsMatch(line) || Hr.IsMatch(line) ||
        Blockquote.IsMatch(line) || UnorderedItem.IsMatch(line) || OrderedItem.IsMatch(line);

    private static string Inline(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        // 1. Protect inline code spans (escaped, not processed further). The
        //    @@CODE<n>@@ token uses chars no inline regex matches, so it survives.
        var placeholders = new List<string>();
        text = InlineCode.Replace(text, m =>
        {
            placeholders.Add("<code>" + Escape(m.Groups[1].Value) + "</code>");
            return $"@@CODE{placeholders.Count - 1}@@";
        });

        // 2. Escape everything else.
        text = Escape(text);

        // 3. Images (before links, since ![ ]( ) would otherwise be caught by links).
        text = Image.Replace(text, m =>
        {
            var alt = m.Groups[1].Value;
            var url = m.Groups[2].Value;
            return SafeImageUrl(url)
                ? $"<img src=\"{EscapeAttr(url)}\" alt=\"{alt}\" style=\"max-width:100%\"/>"
                : alt;
        });

        // 4. Links with protocol filtering.
        text = Link.Replace(text, m =>
        {
            var label = m.Groups[1].Value;
            var url = m.Groups[2].Value;
            return SafeLinkUrl(url)
                ? $"<a href=\"{EscapeAttr(url)}\">{label}</a>"
                : label;
        });

        // 5. Bold, italic, strikethrough.
        text = Bold.Replace(text, m => $"<strong>{(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)}</strong>");
        text = Italic.Replace(text, m => $"<em>{m.Groups[1].Value}</em>");
        text = Strike.Replace(text, m => $"<del>{m.Groups[1].Value}</del>");

        // 6. Restore code spans.
        text = CodePlaceholder.Replace(text, m => placeholders[int.Parse(m.Groups[1].Value)]);
        return text;
    }

    private static bool SafeLinkUrl(string url)
    {
        var lower = url.Trim().ToLowerInvariant();
        if (lower.StartsWith("javascript:") || lower.StartsWith("vbscript:") || lower.StartsWith("data:"))
            return false;
        if (lower.StartsWith("http://") || lower.StartsWith("https://") || lower.StartsWith("mailto:"))
            return true;
        // Relative URL (no scheme) — allow.
        return !Regex.IsMatch(url, @"^[a-z][a-z0-9+.\-]*:", RegexOptions.IgnoreCase);
    }

    private static bool SafeImageUrl(string url)
    {
        var lower = url.Trim().ToLowerInvariant();
        if (lower.StartsWith("http://") || lower.StartsWith("https://")) return true;
        if (lower.StartsWith("data:") || lower.StartsWith("javascript:")) return false;
        return !Regex.IsMatch(url, @"^[a-z][a-z0-9+.\-]*:", RegexOptions.IgnoreCase);
    }

    private static string Escape(string s) => s
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;");

    private static string EscapeAttr(string s) => s.Replace("\"", "&quot;");
}
