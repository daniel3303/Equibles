using AngleSharp.Dom;
using AngleSharp.Html.Dom;

namespace Equibles.Sec.BusinessLogic.Normalizers;

internal static class StylesheetProseConversionStep
{
    private const double CoordinateTolerance = 1;

    public static void Execute(IHtmlDocument original, string markup)
    {
        using var geometry = XhtmlStylesheetGeometry.TryCreate(markup, original);
        if (geometry == null)
            return;
        var originals = original.QuerySelectorAll(".pf > .pc");
        var pages = geometry.Document.QuerySelectorAll(".pf > .pc");
        if (originals.Length != pages.Length)
            return;
        for (var index = 0; index < pages.Length; index++)
            Convert(originals[index], pages[index], original, geometry);
    }

    private static void Convert(
        IElement original,
        IElement page,
        IHtmlDocument document,
        XhtmlStylesheetGeometry geometry
    )
    {
        var originalChildren = original.Children.ToArray();
        var children = page.Children.ToArray();
        if (originalChildren.Length != children.Length || original.TextContent != page.TextContent)
            return;
        for (var ancestor = page; ancestor != null; ancestor = ancestor.ParentElement)
        {
            if (ancestor.HasAttribute("hidden"))
                return;
            var style = geometry.Style(ancestor);
            if (
                new[]
                {
                    "transform",
                    "-webkit-transform",
                    "-ms-transform",
                    "rotate",
                    "scale",
                    "translate",
                    "perspective",
                    "filter",
                }.Any(property => style.GetPropertyValue(property) is not ("" or "none"))
                || style.GetPropertyValue("content-visibility") is not ("" or "visible")
                || style.GetPropertyValue("clip-path") is not ("" or "none")
                || style.GetPropertyValue("clip") is not ("" or "auto")
                || style.GetPropertyValue("display") == "none"
                || style.GetPropertyValue("visibility") is not ("" or "visible")
                || style.GetPropertyValue("opacity") is not ("" or "1")
                || style.GetPropertyValue("zoom") is not ("" or "1" or "normal")
                || style.GetPropertyValue("writing-mode") is not ("" or "horizontal-tb")
                || style.GetPropertyValue("direction") is not ("" or "ltr")
                || style.GetPropertyValue("unicode-bidi")
                    is not ("" or "normal" or "embed" or "isolate" or "bidi-override")
                || style.GetPropertyValue("text-transform") is not ("" or "none")
                || style.GetPropertyValue("text-decoration-line") is not ("" or "none")
                || style.GetPropertyValue("text-decoration") is not ("" or "none")
            )
                return;
        }
        var pageStyle = geometry.Style(page);
        if (
            pageStyle.GetPropertyValue("position") != "absolute"
            || pageStyle.GetPropertyValue("transform") is not ("" or "none")
            || pageStyle.GetPropertyValue("top") is not ("0" or "0px")
            || pageStyle.GetPropertyValue("left") is not ("0" or "0px")
        )
            return;
        var candidates = children
            .Select(child => StylesheetProseLine.Read(child, geometry))
            .ToArray();
        // Every directly positioned text sibling participates, including headings and
        // short numeric cells that can never themselves become paragraph candidates.
        var obstacles = children
            .Where(
                (child, index) =>
                    candidates[index] == null && !string.IsNullOrWhiteSpace(child.TextContent)
            )
            .Select(child => StylesheetProseObstacle.Read(child, page, geometry))
            .Where(value => value != null)
            .ToArray();
        var points = new List<(IElement Element, double Bottom)>();
        foreach (var element in page.QuerySelectorAll("*"))
        {
            if (string.IsNullOrWhiteSpace(element.TextContent))
                continue;
            if (
                obstacles.Any(obstacle =>
                    obstacle.Element == element || obstacle.Element.Contains(element)
                )
            )
                continue;
            if (element.ParentElement?.Closest(".t") is { } owningLine)
            {
                var owningStyle = geometry.Style(owningLine);
                if (
                    !StylesheetLineBox.TryPixel(owningStyle, "font-size", out var owningFont)
                    || !StylesheetProseInline.IsValid(
                        element,
                        owningLine,
                        geometry,
                        owningStyle,
                        owningFont
                    )
                )
                    return;
                continue;
            }
            var style = geometry.Style(element);
            if (
                style.GetPropertyValue("position") is "" or "static"
                && !element.ClassList.Contains("t")
            )
                continue;
            if (
                style.GetPropertyValue("position") != "absolute"
                || element.TextContent.Any(character =>
                    character is '\n' or '\r' or '\u0085' or '\u2028' or '\u2029'
                )
                || !StylesheetProseLine.Known(style)
                || style.GetPropertyValue("top") is not ("" or "auto")
                || !StylesheetLineBox.TryPixel(style, "font-size", out var pointFont)
                || !StylesheetLineBox.IsValid(style, pointFont)
                || !StylesheetProseLine.UnshiftedTransform(style)
                || !StylesheetLineBox.TryPixel(style, "bottom", out var bottom)
                || !StylesheetLineBox.TryPixel(style, "left", out _)
            )
                return;
            for (var parent = element.ParentElement; parent != page; parent = parent.ParentElement)
            {
                var container = geometry.Style(parent);
                if (
                    new[]
                    {
                        "transform",
                        "-webkit-transform",
                        "-ms-transform",
                        "rotate",
                        "scale",
                        "translate",
                        "perspective",
                    }.Any(property => container.GetPropertyValue(property) is not ("" or "none"))
                )
                    return;
                if (container.GetPropertyValue("position") is "" or "static")
                    continue;
                if (
                    container.GetPropertyValue("position") != "absolute"
                    || !StylesheetLineBox.TryPixel(container, "bottom", out var offset)
                )
                    return;
                bottom += offset;
            }
            points.Add((element, bottom));
        }
        var ambiguous = new HashSet<IElement>();
        var ordered = points.OrderBy(point => point.Bottom).ToArray();
        for (var index = 1; index < ordered.Length; index++)
            if (ordered[index].Bottom - ordered[index - 1].Bottom <= CoordinateTolerance)
            {
                ambiguous.Add(ordered[index - 1].Element);
                ambiguous.Add(ordered[index].Element);
            }
        foreach (var line in candidates.Where(line => line != null))
            if (
                obstacles.Any(obstacle =>
                    line.Bottom <= obstacle.Top && line.Bottom + line.FontSize >= obstacle.Bottom
                )
            )
                ambiguous.Add(line.Element);
        var run = new List<(IElement Original, StylesheetProseLine Line)>();
        void Flush()
        {
            if (run.Count >= 3 && run.Sum(row => row.Original.TextContent.Length) <= 8000)
            {
                var gaps = run.Zip(
                        run.Skip(1),
                        (first, next) => first.Line.Bottom - next.Line.Bottom
                    )
                    .ToArray();
                if (gaps.Max() - gaps.Min() <= CoordinateTolerance)
                    Join(run.Select(row => row.Original).ToArray(), document);
            }
            run.Clear();
        }
        for (var index = 0; index < children.Length; index++)
        {
            var line = candidates[index];
            if (
                line == null
                || ambiguous.Contains(line.Element)
                || !Prose(line.Element.TextContent)
                || originalChildren[index].TextContent != line.Element.TextContent
            )
            {
                Flush();
                continue;
            }
            if (
                run.Count > 0
                && (
                    !Continues(run[^1].Line, line)
                    || HasInterveningText(originalChildren[index - 1], originalChildren[index])
                )
            )
                Flush();
            run.Add((originalChildren[index], line));
        }
        Flush();
    }

    private static bool Continues(StylesheetProseLine first, StylesheetProseLine next)
    {
        var gap = first.Bottom - next.Bottom;
        var text = first.Element.TextContent.Trim();
        return Math.Abs(first.Left - next.Left) <= CoordinateTolerance
            && Math.Abs(first.FontSize - next.FontSize) <= 0.01
            && first.Font == next.Font
            && gap >= first.FontSize * 0.8
            && gap <= first.FontSize * 1.6
            && text[^1] is not ('.' or ';' or ':' or '?' or '!');
    }

    private static bool Prose(string text)
    {
        text = text.Trim();
        return text.Length >= 45
            && text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Length >= 5
            && text.Any(char.IsLower)
            && (char.IsLetter(text[0]) || text[0] is '(' or '€' or '$' or '£');
    }

    private static bool HasInterveningText(IElement first, IElement next)
    {
        for (var node = first.NextSibling; node != null && node != next; node = node.NextSibling)
            if (node is not IText text || !string.IsNullOrWhiteSpace(text.Data))
                return true;
        return false;
    }

    private static void Join(IElement[] lines, IHtmlDocument document)
    {
        var paragraph = document.CreateElement("p");
        lines[0].ParentElement.InsertBefore(paragraph, lines[0]);
        foreach (var line in lines)
        {
            if (paragraph.HasChildNodes)
                paragraph.AppendChild(document.CreateElement("br"));
            foreach (var node in line.ChildNodes.ToArray())
                paragraph.AppendChild(node);
            line.Remove();
        }
    }
}
