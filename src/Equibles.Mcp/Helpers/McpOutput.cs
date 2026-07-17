using System.Globalization;

namespace Equibles.Mcp.Helpers;

public static class McpOutput
{
    // Appended after a truncated result set so the model knows more rows exist and which
    // argument raises the cap. Returns the empty string when nothing was cut off, so callers
    // can append it unconditionally.
    public static string TruncationNote(int shown, int total, string argumentName = "maxResults") =>
        shown >= total
            ? string.Empty
            : $"_Showing first {shown} of {total} results - raise {argumentName} to see more._";

    // Strict ISO yyyy-MM-dd parse in invariant culture. Rejects any other format so a typo or
    // locale-shaped date can't silently parse into a different day and shift the requested range.
    public static bool TryParseDate(string input, out DateTime date) =>
        DateTime.TryParseExact(
            input?.Trim(),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date
        );

    // Consistent one-line rejection for an argument value outside the accepted set, so every
    // tool phrases the correction the same way for the model.
    public static string InvalidArgument(string argumentName, string value, string accepted) =>
        $"Unknown {argumentName} '{value}'. Accepted: {accepted}.";
}
