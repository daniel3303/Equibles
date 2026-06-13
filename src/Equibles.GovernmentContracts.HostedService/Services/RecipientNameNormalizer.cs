using System.Text;

namespace Equibles.GovernmentContracts.HostedService.Services;

/// <summary>
/// Normalises a company/recipient legal name to a comparable key for exact-match
/// resolution between USAspending recipients and our <c>CommonStock</c> universe.
/// Deliberately conservative — it strips punctuation, a leading "THE", and trailing
/// legal-entity suffixes (CORP/INC/LLC/…) so "Lockheed Martin Corporation" and
/// "Lockheed Martin Corp" collapse to the same key, but it never fuzzy-matches.
/// Returns null when nothing meaningful remains.
/// </summary>
public static class RecipientNameNormalizer
{
    private const int MinimumKeyLength = 4;

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

        while (tokens.Count > 1 && LegalSuffixes.Contains(tokens[^1]))
            tokens.RemoveAt(tokens.Count - 1);

        var key = string.Join(' ', tokens);
        return key.Length >= MinimumKeyLength ? key : null;
    }
}
