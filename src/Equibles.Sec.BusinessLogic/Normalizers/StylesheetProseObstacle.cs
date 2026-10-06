using AngleSharp.Css.Dom;
using AngleSharp.Dom;

namespace Equibles.Sec.BusinessLogic.Normalizers;

// Reserve the vertical footprint of non-prose content, including a font-height
// margin, without changing it or assuming a horizontal reading order.
internal sealed record StylesheetProseObstacle(IElement Element, double Bottom, double Top)
{
    public static StylesheetProseObstacle Read(
        IElement element,
        IElement page,
        XhtmlStylesheetGeometry geometry
    )
    {
        if (element.ClassList.Contains("t"))
        {
            var line = StylesheetProseLine.ReadObstacle(element, geometry);
            // The line and each inline child permit at most twice the font size
            // for their line box. Reserve both sides of the baseline conservatively.
            return line == null
                ? null
                : new(element, line.Bottom - 2 * line.FontSize, line.Bottom + 2 * line.FontSize);
        }
        var style = geometry.Style(element);
        if (
            style.GetPropertyValue("position") != "absolute"
            || !Flat(style)
            || !StylesheetLineBox.TryPixel(style, "top", out var top)
            || !StylesheetLineBox.TryPixel(style, "left", out _)
            || !StylesheetLineBox.TryPixel(style, "height", out var height)
            || height <= 0
            || !StylesheetLineBox.TryPixel(geometry.Style(page), "height", out var pageHeight)
        )
            return null;
        var rows = new HashSet<IElement>();
        var padding = 0.0;
        foreach (var child in element.QuerySelectorAll("*"))
        {
            var childStyle = geometry.Style(child);
            if (!Flat(childStyle))
                return null;
            var position = childStyle.GetPropertyValue("position");
            if (position is "" or "static")
                continue;
            if (position == "relative")
            {
                if (
                    !ZeroOffsets(childStyle)
                    || !StylesheetLineBox.TryPixel(childStyle, "height", out var childHeight)
                    || Math.Abs(childHeight - height) > 0.01
                )
                    return null;
                continue;
            }
            if (
                position != "absolute"
                || child.TextContent.Any(character =>
                    character is '\n' or '\r' or '\u0085' or '\u2028' or '\u2029'
                )
                || childStyle.GetPropertyValue("bottom") is not ("" or "auto")
                || childStyle.GetPropertyValue("white-space") != "pre"
                || !StylesheetLineBox.TryPixel(childStyle, "top", out var rowTop)
                || rowTop < 0
                || !StylesheetLineBox.TryPixel(childStyle, "left", out _)
                || !StylesheetLineBox.TryPixel(childStyle, "font-size", out var font)
                || font is < 6 or > 64
                || rowTop > height
                || child.ParentElement.Closest(".t") != null
                || rows.Any(row => row.Contains(child))
            )
                return null;
            rows.Add(child);
            padding = Math.Max(padding, font);
        }
        if (rows.Count == 0)
            return null;
        foreach (var child in element.QuerySelectorAll("*").Prepend(element))
        {
            if (
                !child.ChildNodes.OfType<IText>().Any(text => !string.IsNullOrWhiteSpace(text.Data))
            )
                continue;
            var row = rows.SingleOrDefault(row => row == child || row.Contains(child));
            if (row == null || !InlineText(child, row, geometry, ref padding))
                return null;
        }
        return new(element, pageHeight - top - height - padding, pageHeight - top + padding);
    }

    private static bool InlineText(
        IElement child,
        IElement row,
        XhtmlStylesheetGeometry geometry,
        ref double padding
    )
    {
        for (var current = child; current != row; current = current.ParentElement)
        {
            if (
                current.LocalName
                is not ("span" or "a" or "ix:continuation" or "ix:nonfraction" or "ix:nonnumeric")
            )
                return false;
            var style = geometry.Style(current);
            if (
                style.GetPropertyValue("display")
                    is not ("" or "inline" or "inline-block" or "inherit")
                || style.GetPropertyValue("white-space") is not ("" or "pre" or "inherit")
            )
                return false;
            if (style.GetPropertyValue("font-size").Length > 0)
            {
                if (
                    !StylesheetLineBox.TryPixel(style, "font-size", out var font)
                    || font is < 6 or > 64
                )
                    return false;
                padding = Math.Max(padding, font);
            }
        }
        return true;
    }

    private static bool ZeroOffsets(ICssStyleDeclaration style) =>
        new[] { "left", "right", "top", "bottom" }.All(property =>
            style.GetPropertyValue(property) is "" or "auto" or "0" or "0px"
        );

    private static readonly HashSet<string> SupportedProperties = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "position",
        "left",
        "right",
        "top",
        "bottom",
        "width",
        "height",
        "z-index",
        "display",
        "clear",
        "font-family",
        "font-size",
        "font-style",
        "font-weight",
        "white-space",
        "line-height",
        "word-spacing",
        "letter-spacing",
        "margin-left",
        "margin-right",
        "color",
        "text-shadow",
        "-webkit-text-stroke",
        "unicode-bidi",
        "transform",
        "-webkit-transform",
        "-ms-transform",
        "rotate",
        "scale",
        "translate",
        "perspective",
        "filter",
        "padding-top",
        "padding-bottom",
        "margin-top",
        "margin-bottom",
        "border-top-width",
        "border-bottom-width",
        "vertical-align",
        "writing-mode",
        "float",
        "zoom",
    };

    private static bool Flat(ICssStyleDeclaration style) =>
        style.All(property => SupportedProperties.Contains(property.Name))
        && new[]
        {
            "transform",
            "-webkit-transform",
            "-ms-transform",
            "rotate",
            "scale",
            "translate",
            "perspective",
            "filter",
        }.All(property => style.GetPropertyValue(property) is "" or "none")
        && new[]
        {
            "padding-top",
            "padding-bottom",
            "margin-top",
            "margin-bottom",
            "border-top-width",
            "border-bottom-width",
        }.All(property => style.GetPropertyValue(property) is "" or "0" or "0px")
        && style.GetPropertyValue("vertical-align") is "" or "baseline"
        && style.GetPropertyValue("writing-mode") is "" or "horizontal-tb"
        && style.GetPropertyValue("float") is "" or "none"
        && style.GetPropertyValue("display")
            is not ("grid" or "flex" or "table" or "inline-flex" or "inline-grid")
        && style.GetPropertyValue("line-height") is "" or "normal"
        && style.GetPropertyValue("zoom") is "" or "1" or "normal";
}
