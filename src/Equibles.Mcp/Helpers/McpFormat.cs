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

    // Per-share price with magnitude-adaptive decimals: two decimals at or above $1, and
    // below that enough decimals to keep at least two significant digits (capped at 8), so a
    // sub-dollar OHLC row never collapses to one flat value — a $0.0072 close rendered "0.01"
    // was 39% overstated and reported a 20% daily range as flat. The rule follows the VALUE,
    // never a per-tool constant, so every price column shares one behaviour.
    public static string Price(decimal value)
    {
        var abs = Math.Abs(value);
        if (abs >= 1m || abs == 0m)
        {
            return value.ToString("F2", CultureInfo.InvariantCulture);
        }

        var decimals = 2;
        var scaled = abs;
        while (scaled < 0.1m && decimals < 8)
        {
            scaled *= 10m;
            decimals++;
        }
        return value.ToString("F" + decimals, CultureInfo.InvariantCulture);
    }

    // Nullable companion to Price: em-dash when null.
    public static string PriceOrDash(decimal? value) => value.HasValue ? Price(value.Value) : Dash;

    // Signed price delta (change columns): explicit +/− with Price's adaptive decimals, so a
    // sub-dollar move never rounds to a signless 0.00.
    public static string SignedPrice(decimal value) =>
        value switch
        {
            > 0 => "+" + Price(value),
            < 0 => "-" + Price(Math.Abs(value)),
            _ => Price(0m),
        };

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
