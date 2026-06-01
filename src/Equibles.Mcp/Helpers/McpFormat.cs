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
}
