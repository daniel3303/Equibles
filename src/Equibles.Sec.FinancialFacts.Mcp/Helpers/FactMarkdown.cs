using System.Globalization;

namespace Equibles.Sec.FinancialFacts.Mcp.Helpers;

/// <summary>
/// Shared rendering helpers for the FinancialFacts MCP tools so the
/// Markdown-table output (escaping, culture-invariant value formatting,
/// log-context hygiene) is defined once rather than per tool.
/// </summary>
public static class FactMarkdown
{
    /// <summary>
    /// Escapes the Markdown table delimiters so a value containing '|' or a
    /// newline (e.g. some ADR/fund names) can't break the table the LLM reads.
    /// </summary>
    public static string Cell(string value) =>
        value == null ? "" : value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");

    /// <summary>
    /// Strips control chars from untrusted args before they reach logs / the
    /// error store, matching the repo's log-hygiene precedent (e91e72a).
    /// </summary>
    public static string Clean(string value) =>
        value == null ? "" : new string(value.Where(c => !char.IsControl(c)).ToArray());

    /// <summary>
    /// Formats a reported value culture-invariantly. Per-share units get cents
    /// precision; large monetary / share-count units get grouped whole numbers;
    /// dimensionless or ratio units (e.g. <c>pure</c>) keep their fractional
    /// precision rather than being rounded to an integer. Only USD is prefixed
    /// with '$'; other currencies (EUR/GBP for 20-F / 40-F filers) are conveyed
    /// by the unit column rather than mislabelled with a dollar sign.
    /// </summary>
    public static string Value(decimal value, string unit)
    {
        var u = unit?.Trim();
        var isPerShare = u != null && u.EndsWith("/shares", StringComparison.OrdinalIgnoreCase);
        var isUsd = u != null && u.StartsWith("USD", StringComparison.OrdinalIgnoreCase);
        // "shares" is a large integer count; other plain-currency units are
        // large monetary values — both read best as grouped whole numbers.
        var isWholeMagnitude =
            isUsd
            || string.Equals(u, "shares", StringComparison.OrdinalIgnoreCase)
            || (u != null && IsBareCurrency(u));

        string number;
        if (isPerShare)
            number = value.ToString("N2", CultureInfo.InvariantCulture);
        else if (isWholeMagnitude)
            number = value.ToString("N0", CultureInfo.InvariantCulture);
        else
            // pure / dimensionless / ratios — don't destroy the fraction.
            number = value.ToString("0.############", CultureInfo.InvariantCulture);

        return isUsd ? "$" + number : number;
    }

    // A bare 3-letter currency code (USD already handled above): EUR, GBP, JPY…
    private static bool IsBareCurrency(string unit) => unit.Length == 3 && unit.All(char.IsLetter);
}
