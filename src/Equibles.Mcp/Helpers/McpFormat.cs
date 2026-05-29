using System.Globalization;

namespace Equibles.Mcp.Helpers;

public static class McpFormat
{
    private const string Dash = "—";

    // Formats a nullable value with the given format string, or the em-dash placeholder when null.
    // Always formats with InvariantCulture so MCP markdown does not fork the separators by host locale.
    public static string OrDash<T>(T? value, string format)
        where T : struct, IFormattable =>
        value.HasValue ? value.Value.ToString(format, CultureInfo.InvariantCulture) : Dash;
}
