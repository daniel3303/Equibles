using AngleSharp.Css.Dom;
using AngleSharp.Css.Parser;
using AngleSharp.Css.Values;

namespace Equibles.Sec.BusinessLogic.Normalizers;

internal static class InlineXbrlProseDeclaration
{
    public static bool Safe(ICssProperty property, CssParser parser)
    {
        var value = property.Value;
        if (value.Contains("var(", StringComparison.OrdinalIgnoreCase))
            return false;
        return property.Name switch
        {
            "display" => value is "block" or "inline" or "inline-block" or "contents",
            "visibility" => value == "visible",
            "content-visibility" => value is "visible" or "auto",
            "opacity" => value == "1",
            "overflow" or "overflow-x" or "overflow-y" => value == "visible",
            "content" => value is "none" or "normal" or "\"\"" or "''",
            "filter"
            or "clip-path"
            or "text-decoration"
            or "text-decoration-line"
            or "text-transform" => value == "none",
            "clip" => value == "auto",
            "direction" => value == "ltr",
            "unicode-bidi" => value is "normal" or "embed" or "isolate",
            "color" => property.RawValue is CssColorValue color && color.Alpha == 1,
            "font-size" or "width" or "height" => PositiveLength(property.RawValue),
            "line-height" => value == "normal"
                || PositiveLength(property.RawValue)
                || property.RawValue is CssNumberValue number && number.Value > 0,
            "letter-spacing" or "word-spacing" => value == "normal"
                || property.RawValue is CssLengthValue spacing
                    && spacing.IsAbsolute
                    && double.IsFinite(spacing.Value)
                    && Math.Abs(spacing.Value) <= 10,
            "transform" or "-webkit-transform" or "-ms-transform" => Transform(value, parser),
            "position" => value is "static" or "relative" or "absolute",
            "white-space" => value is "normal" or "nowrap" or "pre" or "pre-wrap" or "pre-line",
            "font-family"
            or "font-style"
            or "font-weight"
            or "font-feature-settings"
            or "text-rendering"
            or "text-align"
            or "vertical-align"
            or "transform-origin"
            or "-webkit-transform-origin"
            or "-ms-transform-origin"
            or "background-color"
            or "box-shadow"
            or "border-collapse"
            or "border-top-width"
            or "border-right-width"
            or "border-bottom-width"
            or "border-left-width"
            or "border-top-style"
            or "border-right-style"
            or "border-bottom-style"
            or "border-left-style"
            or "border-top-color"
            or "border-right-color"
            or "border-bottom-color"
            or "border-left-color"
            or "margin-top"
            or "margin-right"
            or "margin-bottom"
            or "margin-left"
            or "padding-top"
            or "padding-right"
            or "padding-bottom"
            or "padding-left"
            or "top"
            or "left"
            or "bottom"
            or "right" => true,
            _ => false,
        };
    }

    private static bool PositiveLength(ICssValue value) =>
        value is CssLengthValue length
        && length.IsAbsolute
        && double.IsFinite(length.Value)
        && length.Value > 0;

    private static bool Transform(string value, CssParser parser)
    {
        if (value == "none")
            return true;
        if (value.Contains('%') || value.Contains("calc(", StringComparison.OrdinalIgnoreCase))
            return false;
        var property = parser.ParseDeclaration("transform:" + value).GetProperty("transform");
        if (property?.RawValue is not CssTupleValue tuple || tuple.Items.Length > 16)
            return false;
        foreach (var item in tuple.Items)
        {
            if (item is CssRotateValue rotation)
            {
                if (rotation.Angle.AsRad() != 0)
                    return false;
                continue;
            }
            if (
                item is not (CssTranslateValue or CssScaleValue or CssSkewValue)
                || item is not ICssTransformFunctionValue function
            )
                return false;
            if (
                function is CssTranslateValue translation
                && new[] { translation.ShiftX, translation.ShiftY, translation.ShiftZ }.Any(shift =>
                    shift != null && (shift is not CssLengthValue length || !length.IsAbsolute)
                )
            )
                return false;
            var matrix = function.ComputeMatrix(null);
            if (
                matrix == null
                || matrix.M11 is not (>= 0.01 and <= 100)
                || matrix.M22 is not (>= 0.01 and <= 100)
                || matrix.M33 != 1
                || matrix.M12 != 0
                || matrix.M13 != 0
                || matrix.M21 != 0
                || matrix.M23 != 0
                || matrix.M31 != 0
                || matrix.M32 != 0
                || matrix.Tz != 0
                || !double.IsFinite(matrix.Tx)
                || !double.IsFinite(matrix.Ty)
            )
                return false;
        }
        return true;
    }
}
