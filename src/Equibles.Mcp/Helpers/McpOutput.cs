using System.Globalization;

namespace Equibles.Mcp.Helpers;

public static class McpOutput
{
    // Appended after a truncated result set so the model knows more rows exist and which
    // argument raises the cap. Returns the empty string when nothing was cut off, so callers
    // can append it unconditionally. When the shown count already sits AT the cap, "raise
    // maxResults" is impossible advice (the argument is maxed), so the note states the cap
    // and points at the tool's real continuation instead — callers whose tools have a
    // narrower escape hatch (a date range, a filter) pass it via atCapAdvice.
    public static string TruncationNote(
        int shown,
        int total,
        string argumentName = "maxResults",
        int cap = McpLimit.MaxResults,
        string atCapAdvice = null
    )
    {
        if (shown >= total)
            return string.Empty;
        if (shown < cap)
            return $"_Showing first {shown} of {total} results - raise {argumentName} to see more._";
        var advice = atCapAdvice ?? "narrow the request (filters or date range) to see more";
        return $"_Showing first {shown} of {total} results - {argumentName} is at its cap of {cap}; {advice}._";
    }

    // Truncation note for tools that page with an offset argument. Names the absolute row
    // range shown so the model can stitch pages, and always offers a continuation that is
    // actually possible: raise the cap argument while below the cap, or continue at the next
    // offset once at it. Empty when the full set (from offset 0) was shown, so callers can
    // append it unconditionally.
    public static string PagedTruncationNote(
        int shown,
        int total,
        int offset,
        string argumentName = "maxResults",
        int cap = McpLimit.MaxResults
    )
    {
        if (shown == 0)
            return offset > 0
                ? $"_No results at offset {offset} - only {total} results exist; lower offset._"
                : string.Empty;

        var first = offset + 1;
        var last = offset + shown;
        if (offset == 0 && last >= total)
            return string.Empty;
        if (last >= total)
            return $"_Showing results {first}-{last} of {total} (the last page)._";
        var raise = shown < cap ? $"raise {argumentName} (max {cap}) or " : "";
        return $"_Showing results {first}-{last} of {total} - {raise}pass offset={last} to continue._";
    }

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
