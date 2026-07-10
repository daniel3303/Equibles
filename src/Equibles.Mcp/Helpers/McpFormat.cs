using System.Globalization;
using System.Numerics;

namespace Equibles.Mcp.Helpers;

public static class McpFormat
{
    private const string Dash = "—";

    // Formats a nullable value with the given format string, or the em-dash placeholder when null.
    // Always formats with InvariantCulture so MCP markdown does not fork the separators by host locale.
    public static string OrDash<T>(T? value, string format)
        where T : struct, IFormattable =>
        value.HasValue ? value.Value.ToString(format, CultureInfo.InvariantCulture) : Dash;

    // Whole numbers (share counts, position counts, whole-dollar amounts) rendered with
    // thousands separators in invariant culture so MCP markdown does not fork the separators
    // by host locale.
    public static string WholeNumber<T>(T value)
        where T : INumber<T> => value.ToString("N0", CultureInfo.InvariantCulture);

    // Non-nullable companion to OrDash: formats a value with the given format string in
    // invariant culture so MCP markdown does not fork the separators by host locale.
    public static string Invariant<T>(T value, string format)
        where T : IFormattable => value.ToString(format, CultureInfo.InvariantCulture);

    // Compact USD for wide-magnitude dollar figures (market caps, dollar volumes):
    // $1.23T / $45.6B / $789M / $12.3K, em-dash when null. Invariant culture so MCP
    // markdown does not fork the decimal separator by host locale.
    public static string CompactUsd(double? value)
    {
        if (value == null)
        {
            return Dash;
        }

        var abs = Math.Abs(value.Value);
        var (scaled, suffix) = abs switch
        {
            >= 1e12 => (value.Value / 1e12, "T"),
            >= 1e9 => (value.Value / 1e9, "B"),
            >= 1e6 => (value.Value / 1e6, "M"),
            >= 1e3 => (value.Value / 1e3, "K"),
            _ => (value.Value, ""),
        };
        return "$" + scaled.ToString("0.##", CultureInfo.InvariantCulture) + suffix;
    }
}
