using System.Globalization;
using AngleSharp.Css.Dom;

namespace Equibles.Sec.BusinessLogic.Normalizers;

internal static class StylesheetLineBox
{
    public static bool IsValid(ICssStyleDeclaration style, double fontSize)
    {
        if (
            style.GetPropertyValue("height") is not ("" or "auto")
            && (
                !TryPixel(style, "height", out var height)
                || height < fontSize * .5
                || height > fontSize * 2
            )
        )
            return false;
        var lineHeight = style.GetPropertyValue("line-height");
        if (lineHeight is "" or "normal")
            return true;
        if (TryPixel(style, "line-height", out var pixels))
            return pixels >= fontSize * .5 && pixels <= fontSize * 2;
        return double.TryParse(
                lineHeight,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var factor
            )
            && double.IsFinite(factor)
            && factor is >= .5 and <= 2;
    }

    internal static bool TryPixel(ICssStyleDeclaration style, string property, out double value)
    {
        var raw = style.GetPropertyValue(property);
        value = 0;
        if (raw == "0")
            return true;
        return raw.EndsWith("px", StringComparison.Ordinal)
            && double.TryParse(
                raw.AsSpan(0, raw.Length - 2),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value
            )
            && double.IsFinite(value)
            && Math.Abs(value) <= 100000;
    }
}
