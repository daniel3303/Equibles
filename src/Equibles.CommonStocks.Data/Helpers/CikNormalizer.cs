namespace Equibles.CommonStocks.Data.Helpers;

public static class CikNormalizer
{
    public const int MaxLength = 16;

    // Validates a caller/provider CIK while preserving its stored zero-padding.
    public static string Validate(string cik)
    {
        if (string.IsNullOrWhiteSpace(cik))
            return null;

        var digits = cik.Trim();
        return
            digits.Length <= MaxLength
            && digits.Any(character => character != '0')
            && digits.All(char.IsAsciiDigit)
            ? digits
            : null;
    }

    // Canonical identity form for comparing differently padded authoritative CIKs.
    public static string Canonicalize(string cik) => Validate(cik)?.TrimStart('0');
}
