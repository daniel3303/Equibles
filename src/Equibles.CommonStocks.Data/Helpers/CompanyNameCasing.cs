using System.Globalization;
using System.Text.RegularExpressions;

namespace Equibles.CommonStocks.Data.Helpers;

/// <summary>
/// Display casing for a company name SEC states in capitals: an all-caps name is title-cased
/// while abbreviations and Roman numerals stay upper case; a name with any lowercase is kept.
/// </summary>
public static class CompanyNameCasing
{
    private static readonly HashSet<string> UpperCaseAbbreviations = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "LLC",
        "LLP",
        "LP",
        "PLC",
        "NV",
        "SA",
        "AG",
        "SE",
        "AB",
        "ASA",
        "ETF",
        "ADR",
        "REIT",
        "USA",
        "US",
        "UK",
    };

    private static readonly Regex RomanNumeralPattern = new(
        @"^M{0,3}(CM|CD|D?C{0,3})(XC|XL|L?X{0,3})(IX|IV|V?I{0,3})$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    // Three-letter English words that happen to satisfy the Roman regex
    // (MIX=1009, DIV=504, LIV=54, CIV=104). Listing them explicitly keeps
    // legitimate short numerals like XL (40), XC (90), CD (400), CM (900),
    // and combos (XLI, XLV, MII) working.
    private static readonly HashSet<string> RomanNumeralFalsePositives = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "MIX",
        "DIV",
        "LIV",
        "CIV",
    };

    private static bool IsRomanNumeral(string token) =>
        RomanNumeralPattern.IsMatch(token) && !RomanNumeralFalsePositives.Contains(token);

    public static string Normalize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return name;

        if (name.Any(char.IsLower))
            return name;

        var titleCased = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.ToLowerInvariant());

        var words = titleCased.Split(' ');
        for (var i = 0; i < words.Length; i++)
        {
            var stripped = words[i].TrimStart('(').TrimEnd('.', ',', ';', ')');
            if (
                stripped.Length > 0
                && (UpperCaseAbbreviations.Contains(stripped) || IsRomanNumeral(stripped))
            )
            {
                words[i] = words[i].ToUpperInvariant();
            }
        }

        return string.Join(' ', words);
    }
}
