using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Equibles.Web.Extensions;

public static partial class MarkdownExtensions
{
    // Matches a pipe table row, a blank line, then another pipe table row
    [GeneratedRegex(@"(\|[^\n]*\|)\n\n(\|)")]
    private static partial Regex BlankLineBetweenPipeRows();

    // Matches a leading URI scheme (RFC 3986 scheme grammar), e.g. "javascript:"
    [GeneratedRegex(@"^\s*([a-zA-Z][a-zA-Z0-9+.\-]*):")]
    private static partial Regex UriSchemeRegex();

    private static readonly HashSet<string> AllowedUriSchemes = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "http",
        "https",
        "mailto",
    };

    // Markdig is not an HTML sanitizer and .DisableHtml() does not cover link
    // destinations, so a markdown link/image with a javascript:/data:/vbscript:
    // scheme renders an active href on ingested untrusted content (stored XSS).
    // Relative URLs and fragments (no scheme) are left intact.
    private static bool IsSafeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return true;
        var match = UriSchemeRegex().Match(url);
        if (!match.Success)
            return true;
        return AllowedUriSchemes.Contains(match.Groups[1].Value);
    }

    public static IHtmlContent MarkdownToHtml(this IHtmlHelper htmlHelper, string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return htmlHelper.Raw(string.Empty);
        }

        // Collapse blank lines between consecutive pipe table rows (fixes old stored data)
        string previous;
        do
        {
            previous = markdown;
            markdown = BlankLineBetweenPipeRows().Replace(markdown, "$1\n$2");
        } while (markdown != previous);

        var pipeline = new MarkdownPipelineBuilder()
            .UsePipeTables()
            .UseGridTables()
            .DisableHtml()
            .Build();

        // Sanitize link/image destinations: parse to the AST and neutralize any
        // URL whose scheme is not allowlisted before rendering (.DisableHtml()
        // only strips raw HTML tags, not markdown link schemes).
        var document = Markdown.Parse(markdown, pipeline);
        foreach (var link in document.Descendants<LinkInline>())
        {
            if (!IsSafeUrl(link.Url))
                link.Url = string.Empty;
        }

        return htmlHelper.Raw(document.ToHtml(pipeline));
    }
}
