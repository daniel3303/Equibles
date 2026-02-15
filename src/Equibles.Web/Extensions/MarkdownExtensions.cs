using System.Text.RegularExpressions;
using Markdig;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Equibles.Web.Extensions;

public static partial class MarkdownExtensions {
    // Matches a pipe table row, a blank line, then another pipe table row
    [GeneratedRegex(@"(\|[^\n]*\|)\n\n(\|)")]
    private static partial Regex BlankLineBetweenPipeRows();

    public static IHtmlContent MarkdownToHtml(this IHtmlHelper htmlHelper, string markdown) {
        if (string.IsNullOrEmpty(markdown)) {
            return htmlHelper.Raw(string.Empty);
        }

        // Collapse blank lines between consecutive pipe table rows (fixes old stored data)
        string previous;
        do {
            previous = markdown;
            markdown = BlankLineBetweenPipeRows().Replace(markdown, "$1\n$2");
        } while (markdown != previous);

        var pipeline = new MarkdownPipelineBuilder()
            .UsePipeTables()
            .UseGridTables()
            .DisableHtml()
            .Build();
        return htmlHelper.Raw(Markdown.ToHtml(markdown, pipeline));
    }
}
