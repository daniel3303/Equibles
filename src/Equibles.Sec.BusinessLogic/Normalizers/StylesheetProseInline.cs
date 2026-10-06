using AngleSharp.Css.Dom;
using AngleSharp.Dom;

namespace Equibles.Sec.BusinessLogic.Normalizers;

internal static class StylesheetProseInline
{
    internal static bool IsValid(
        IElement child,
        IElement line,
        XhtmlStylesheetGeometry geometry,
        ICssStyleDeclaration parentStyle,
        double fontSize,
        bool allowFontVariation = false
    )
    {
        if (child.HasAttribute("hidden"))
            return false;
        var name = child.LocalName.ToLowerInvariant();
        if (
            name
            is not (
                "span"
                or "a"
                or "b"
                or "strong"
                or "i"
                or "em"
                or "ix:continuation"
                or "ix:nonfraction"
                or "ix:nonnumeric"
            )
        )
            return false;
        var style = geometry.Style(child);
        if (
            !StylesheetProseLine.Known(style)
            || style.GetPropertyValue("position") is not ("" or "static" or "relative")
            || !StylesheetProseLine.Unshifted(style, "left", "right", "top", "bottom")
            || style.GetPropertyValue("transform") is not ("" or "none")
            || style.GetPropertyValue("-webkit-transform") is not ("" or "none")
            || style.GetPropertyValue("-ms-transform") is not ("" or "none")
        )
            return false;
        var display = style.GetPropertyValue("display");
        for (
            var parent = child.ParentElement;
            display == "inherit" && parent != null;
            parent = parent.ParentElement
        )
        {
            if (parent == line)
                return false;
            display = geometry.Style(parent).GetPropertyValue("display");
        }
        if (display is not ("" or "inline" or "inline-block" or "contents"))
            return false;
        if (
            style.GetPropertyValue("white-space") is not ("" or "pre" or "inherit")
            || !StylesheetProseLine.Unshifted(style, "height")
            || (
                style.GetPropertyValue("line-height") is not ("" or "inherit")
                && (
                    allowFontVariation
                        ? !StylesheetLineBox.IsValid(style, fontSize)
                        : style.GetPropertyValue("line-height")
                            != parentStyle.GetPropertyValue("line-height")
                )
            )
            || !StylesheetProseLine.Spacing(style, "word-spacing", fontSize)
            || !StylesheetProseLine.Spacing(style, "letter-spacing", fontSize / 4)
        )
            return false;
        foreach (var property in new[] { "font-family", "font-size", "font-weight", "font-style" })
        {
            var value = style.GetPropertyValue(property);
            if (
                value.Length > 0
                && value != parentStyle.GetPropertyValue(property)
                && (!allowFontVariation || property == "font-size")
            )
                return false;
        }
        foreach (var property in new[] { "width", "margin-left", "margin-right" })
        {
            var value = style.GetPropertyValue(property);
            if (value is "" or "auto" or "0" or "0px")
                continue;
            if (
                !string.IsNullOrWhiteSpace(child.TextContent)
                || !StylesheetLineBox.TryPixel(style, property, out var pixels)
                || Math.Abs(pixels) > fontSize
            )
                return false;
        }
        return true;
    }
}
