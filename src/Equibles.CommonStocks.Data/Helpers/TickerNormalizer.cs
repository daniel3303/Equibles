namespace Equibles.CommonStocks.Data.Helpers;

public static class TickerNormalizer
{
    public const int MaxListedLength = 32;
    public const int MaxPrimaryLength = 16;

    // Canonical ticker form for case-insensitive lookups. Invalid or oversized symbols fail
    // closed so request-facing resolvers never query arbitrary text as a security identifier.
    public static string Normalize(string ticker) => NormalizeListed(ticker);

    /// <summary>
    /// Canonical exact-listing symbol accepted from authoritative directories. Every symbol is
    /// bounded to the database/API contract and contains only ASCII letters, digits, dots, and
    /// dashes with at least one alphanumeric character.
    /// </summary>
    public static string NormalizeListed(string ticker)
    {
        if (string.IsNullOrWhiteSpace(ticker))
            return null;

        var trimmed = ticker.Trim();
        if (
            trimmed.Length > MaxListedLength
            || !trimmed.Any(character => char.IsAsciiLetterOrDigit(character))
            || trimmed.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character != '-' && character != '.'
            )
        )
            return null;

        return trimmed.ToUpperInvariant();
    }

    public static string NormalizeDashListed(string ticker) =>
        NormalizeListed(ticker)?.Replace('.', '-');

    /// <summary>
    /// Canonical identity used when comparing symbols across sources whose class separators
    /// differ (for example BRK.B and BRK-B). This form is for equality only, not display.
    /// </summary>
    public static string NormalizeIdentity(string ticker)
    {
        if (string.IsNullOrWhiteSpace(ticker))
            return null;

        var trimmed = ticker.Trim();
        if (
            trimmed.Equals("n/a", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("none", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("not applicable", StringComparison.OrdinalIgnoreCase)
            || trimmed.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character != '-'
                && character != '.'
                && character != '/'
                && character != ' '
            )
        )
            return null;

        var normalized = trimmed
            .ToUpperInvariant()
            .Replace(".", string.Empty)
            .Replace("-", string.Empty)
            .Replace("/", string.Empty)
            .Replace(" ", string.Empty);
        return normalized.Length is > 0 and <= MaxListedLength ? normalized : null;
    }

    public static string NormalizePrimary(string ticker)
    {
        var normalized = NormalizeListed(ticker);
        return normalized?.Length <= MaxPrimaryLength ? normalized : null;
    }
}
