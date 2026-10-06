using System.Globalization;
using AngleSharp.Css.Dom;
using AngleSharp.Dom;

namespace Equibles.Sec.BusinessLogic.Normalizers;

internal sealed record StylesheetProseLine(
    IElement Element,
    double Left,
    double Bottom,
    double FontSize,
    string Font
)
{
    private static readonly HashSet<string> SupportedProperties = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "display",
        "position",
        "left",
        "right",
        "top",
        "bottom",
        "width",
        "height",
        "clear",
        "float",
        "white-space",
        "transform",
        "transform-origin",
        "-ms-transform",
        "-webkit-transform",
        "-ms-transform-origin",
        "-webkit-transform-origin",
        "unicode-bidi",
        "direction",
        "font-family",
        "font-size",
        "font-style",
        "font-weight",
        "line-height",
        "font-feature-settings",
        "-moz-font-feature-settings",
        "visibility",
        "letter-spacing",
        "word-spacing",
        "color",
        "text-shadow",
        "z-index",
        "margin-left",
        "margin-right",
        "text-decoration",
        "text-decoration-line",
        "text-transform",
        "opacity",
        "-webkit-text-stroke",
    };

    public static StylesheetProseLine Read(IElement element, XhtmlStylesheetGeometry geometry) =>
        Read(element, geometry, false);

    // A mixed-font line is never prose. Its unchanged, bounded line box can only
    // reserve space so unrelated prose elsewhere on the page remains readable.
    internal static StylesheetProseLine ReadObstacle(
        IElement element,
        XhtmlStylesheetGeometry geometry
    ) => Read(element, geometry, true);

    private static StylesheetProseLine Read(
        IElement element,
        XhtmlStylesheetGeometry geometry,
        bool allowFontVariation
    )
    {
        // Exported bidi overrides preserve order only for this bounded Latin text
        // subset; mixed scripts and directional controls need a visual reader.
        if (
            element.HasAttribute("hidden")
            || element.TextContent.Any(character =>
                char.IsControl(character) || character is '\u2028' or '\u2029'
            )
            || element.LocalName != "div"
            || !element.ClassList.Contains("t")
            || element.Closest("table, li, ul, ol, pre") != null
            || !element.TextContent.All(character =>
                character <= '\u024f'
                || char.IsWhiteSpace(character)
                || character is >= '\u2010' and <= '\u2029' or >= '\u20a0' and <= '\u20cf'
            )
        )
            return null;
        var style = geometry.Style(element);
        if (
            !Known(style)
            || style.GetPropertyValue("position") != "absolute"
            || style.GetPropertyValue("white-space") != "pre"
            || style.GetPropertyValue("display") is not ("block" or "inline" or "inline-block")
            || style.GetPropertyValue("top") is not ("" or "auto")
            || !Unshifted(style, "top", "right", "margin-left", "margin-right")
            || !StylesheetLineBox.TryPixel(style, "left", out var left)
            || !StylesheetLineBox.TryPixel(style, "bottom", out var bottom)
            || !StylesheetLineBox.TryPixel(style, "font-size", out var fontSize)
            || !StylesheetLineBox.IsValid(style, fontSize)
            || !Spacing(style, "word-spacing", fontSize)
            || !Spacing(style, "letter-spacing", fontSize / 4)
            || !TryScale(style, out var scale)
        )
            return null;
        var effectiveFont = fontSize * scale;
        if (left < 0 || bottom < 0 || effectiveFont is < 6 or > 64)
            return null;
        foreach (var child in element.QuerySelectorAll("*"))
            if (
                !StylesheetProseInline.IsValid(
                    child,
                    element,
                    geometry,
                    style,
                    fontSize,
                    allowFontVariation
                )
            )
                return null;
        var font = string.Join(
            "|",
            new[]
            {
                "font-family",
                "font-weight",
                "font-style",
                "transform",
                "height",
                "line-height",
            }.Select(style.GetPropertyValue)
        );
        return new(element, left, bottom, effectiveFont, font);
    }

    internal static bool Known(ICssStyleDeclaration style) =>
        style.All(property => SupportedProperties.Contains(property.Name))
        && style.GetPropertyValue("visibility") is "" or "visible"
        && style.GetPropertyValue("direction") is "" or "ltr"
        && style.GetPropertyValue("unicode-bidi")
            is ""
                or "normal"
                or "embed"
                or "isolate"
                or "bidi-override"
        && style.GetPropertyValue("float") is "" or "none"
        && style.GetPropertyValue("opacity") is "" or "1"
        && style.GetPropertyValue("text-decoration") is "" or "none"
        && style.GetPropertyValue("text-decoration-line") is "" or "none"
        && style.GetPropertyValue("text-transform") is "" or "none"
        && (
            style.GetPropertyValue("-webkit-text-stroke") is ""
            || style
                .GetPropertyValue("-webkit-text-stroke")
                .EndsWith("transparent", StringComparison.Ordinal)
        );

    internal static bool Unshifted(ICssStyleDeclaration style, params string[] properties) =>
        properties.All(property =>
            style.GetPropertyValue(property) is "" or "auto" or "0" or "0px"
        );

    internal static bool Spacing(ICssStyleDeclaration style, string property, double maximum) =>
        style.GetPropertyValue(property) is "" or "normal" or "0" or "0px"
        || StylesheetLineBox.TryPixel(style, property, out var value) && Math.Abs(value) <= maximum;

    internal static bool UnshiftedTransform(ICssStyleDeclaration style)
    {
        if (
            new[] { "rotate", "scale", "translate", "perspective" }.Any(property =>
                style.GetPropertyValue(property) is not ("" or "none")
            )
        )
            return false;
        return style.GetPropertyValue("transform") is "" or "none"
            ? style.GetPropertyValue("-webkit-transform") is "" or "none"
                && style.GetPropertyValue("-ms-transform") is "" or "none"
            : TryScale(style, out _);
    }

    private static bool TryScale(ICssStyleDeclaration style, out double scale)
    {
        if (
            !Scale(style.GetPropertyValue("transform"), out scale)
            || style.GetPropertyValue("transform-origin") is not ("left bottom" or "0px 100%")
        )
            return false;
        foreach (var prefix in new[] { "-webkit-", "-ms-" })
        {
            var transform = style.GetPropertyValue(prefix + "transform");
            if (
                transform.Length > 0
                && (
                    !Scale(transform, out var vendorScale)
                    || Math.Abs(vendorScale - scale) > 0.000001
                )
            )
                return false;
            var origin = style.GetPropertyValue(prefix + "transform-origin");
            if (origin.Length > 0 && origin is not ("left bottom" or "0px 100%" or "0 100%"))
                return false;
        }
        return true;
    }

    private static bool Scale(string value, out double scale)
    {
        scale = 0;
        if (!value.StartsWith("matrix(", StringComparison.Ordinal) || !value.EndsWith(')'))
            return false;
        var parts = value[7..^1].Split(',');
        if (parts.Length != 6)
            return false;
        var numbers = new double[6];
        for (var index = 0; index < parts.Length; index++)
            if (
                !double.TryParse(
                    parts[index],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out numbers[index]
                ) || !double.IsFinite(numbers[index])
            )
                return false;
        scale = numbers[0];
        return scale is > 0 and <= 8
            && numbers[3] == scale
            && numbers[1] == 0
            && numbers[2] == 0
            && numbers[4] == 0
            && numbers[5] == 0;
    }
}
