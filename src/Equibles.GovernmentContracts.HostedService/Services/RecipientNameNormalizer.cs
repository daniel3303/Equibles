using System.Text;
using System.Text.RegularExpressions;

namespace Equibles.GovernmentContracts.HostedService.Services;

/// <summary>
/// Normalises a company/recipient legal name to a comparable key for exact-match
/// resolution between USAspending recipients and our <c>CommonStock</c> universe.
/// Deliberately conservative — it strips punctuation, a leading "THE", EDGAR's
/// slash-wrapped incorporation markers ("/DE/", "/PA/", "/NEW/"), and trailing
/// legal-entity suffixes (CORP/INC/LLC/…) so "Lockheed Martin Corporation" and
/// "Lockheed Martin Corp" collapse to the same key, but it never fuzzy-matches.
/// Returns null when nothing meaningful remains.
/// </summary>
public static partial class RecipientNameNormalizer
{
    // Two characters, not more: with matching kept exact and ambiguous keys dropped by the
    // resolver, short DISTINCTIVE names ("3M", "RTX", "V2X") are safe, and a floor of 4 was
    // silently excluding those companies from resolution entirely. Single characters stay
    // out — a one-letter key carries no identity.
    private const int MinimumKeyLength = 2;

    // EDGAR registrant names carry a slash-wrapped state-of-incorporation or vintage marker
    // ("Caci International Inc /De/", "Nve Corp /New/", "Fnb Corp/Pa/") that USAspending
    // recipient names never do; left in place it lands a stray trailing token in the key and
    // defeats the exact match. Removing the marker is a mechanical format rule, not a
    // heuristic — matching stays exact on what remains.
    [GeneratedRegex("/[A-Za-z ]{1,8}/")]
    private static partial Regex EdgarSlashMarker();

    private static readonly HashSet<string> LegalSuffixes = new(StringComparer.Ordinal)
    {
        "CORP",
        "CORPORATION",
        "INC",
        "INCORPORATED",
        "CO",
        "COMPANY",
        "LLC",
        "LP",
        "LLP",
        "LTD",
        "LIMITED",
        "PLC",
        "NV",
        "SA",
        "AG",
        "SE",
        "CV",
    };

    public static string Normalize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        name = EdgarSlashMarker().Replace(name, " ");

        var cleaned = new StringBuilder(name.Length);
        foreach (var c in name.ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(c))
                cleaned.Append(c);
            else if (char.IsWhiteSpace(c) || c is '.' or ',' or '-' or '/' or '&' or '\'' or '"')
                cleaned.Append(' ');
            // any other punctuation is dropped entirely
        }

        var tokens = cleaned
            .ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (tokens.Count > 1 && tokens[0] == "THE")
            tokens.RemoveAt(0);

        // Strip every trailing legal suffix, including the last remaining token: a name that is
        // nothing but suffixes ("Corporation", "The Company") must reduce to an empty key so it
        // falls through to null below, rather than leaking a generic word into exact-match resolution.
        while (tokens.Count > 0 && LegalSuffixes.Contains(tokens[^1]))
            tokens.RemoveAt(tokens.Count - 1);

        var key = string.Join(' ', tokens);
        return key.Length >= MinimumKeyLength ? key : null;
    }
}
