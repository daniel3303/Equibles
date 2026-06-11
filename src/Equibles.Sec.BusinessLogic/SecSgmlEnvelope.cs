namespace Equibles.Sec.BusinessLogic;

// Shared SGML-envelope reading primitives for SEC full-submission text. SEC filings wrap
// their documents in an SGML envelope whose header tags (<TYPE>, <FILENAME>, ...) carry a
// single-line value rather than a closing tag, so the value runs to the next line break or
// angle bracket.
internal static class SecSgmlEnvelope
{
    // Reads the single-line value of an SGML header tag (e.g. <FILENAME>edgar.htm). The value
    // ends at the first line break or '<', is trimmed, and only its first whitespace-delimited
    // token is returned — SEC sometimes appends a descriptive trailer after the bare value.
    public static bool TryGetTagValue(string block, string tagName, out string value)
    {
        value = string.Empty;
        var tagMarker = $"<{tagName}>";
        var idx = block.IndexOf(tagMarker, StringComparison.OrdinalIgnoreCase);
        if (idx == -1)
            return false;

        var valueStart = idx + tagMarker.Length;
        var end = valueStart;
        while (end < block.Length && block[end] != '\n' && block[end] != '\r' && block[end] != '<')
        {
            end++;
        }

        var raw = block.Substring(valueStart, end - valueStart).Trim();
        if (raw.Length == 0)
            return false;

        value = raw.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)[0];
        return true;
    }

    // Reads the full single-line value of an SGML header tag, whitespace-normalized but with
    // every token kept. Multi-word form types ("DEF 14A") need the whole line — the caller
    // decides how much of it is the value versus a descriptive trailer.
    public static bool TryGetTagLine(string block, string tagName, out string value)
    {
        value = string.Empty;
        var tagMarker = $"<{tagName}>";
        var idx = block.IndexOf(tagMarker, StringComparison.OrdinalIgnoreCase);
        if (idx == -1)
            return false;

        var valueStart = idx + tagMarker.Length;
        var end = valueStart;
        while (end < block.Length && block[end] != '\n' && block[end] != '\r' && block[end] != '<')
        {
            end++;
        }

        var tokens = block
            .Substring(valueStart, end - valueStart)
            .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return false;

        value = string.Join(' ', tokens);
        return true;
    }
}
