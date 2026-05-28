namespace Equibles.Mcp.Helpers;

public static class McpFormat
{
    private const string Dash = "—";

    // Formats a nullable value with the given format string, or the em-dash placeholder when null.
    // A null format provider resolves to CurrentCulture, matching a direct value.ToString(format) call.
    public static string OrDash<T>(T? value, string format)
        where T : struct, IFormattable =>
        value.HasValue ? value.Value.ToString(format, null) : Dash;
}
