namespace Equibles.Core.Identity;

public static class InternationalSecurityIdentifiers
{
    // ISO 6166 identifier syntax/check digit, not instrument classification.
    public static bool IsValidIsin(string value)
    {
        if (
            value?.Length != 12
            || value[0] is < 'A' or > 'Z'
            || value[1] is < 'A' or > 'Z'
            || value[^1] is < '0' or > '9'
        )
            return false;
        var digits = new List<int>();
        foreach (var character in value)
        {
            if (character is >= '0' and <= '9')
                digits.Add(character - '0');
            else if (character is >= 'A' and <= 'Z')
            {
                var number = character - 'A' + 10;
                digits.Add(number / 10);
                digits.Add(number % 10);
            }
            else
                return false;
        }
        var sum = 0;
        for (var index = digits.Count - 1; index >= 0; index--)
        {
            var digit = digits[index] * ((digits.Count - 1 - index) % 2 == 0 ? 1 : 2);
            sum += digit / 10 + digit % 10;
        }
        return sum % 10 == 0;
    }

    // ANNA names CUSIP Global Services the substitute numbering agency for these jurisdictions, so their ISINs embed a
    // CINS whose first six characters number the issuer. US and Canadian CUSIPs keep their own SEC identity paths.
    private static readonly HashSet<string> CusipNumberedJurisdictions =
    [
        .. "AG AI AS AW BL BM BS BZ CW DM FM GD GS GY HT KN KY LC MF MH MP PR PW SR SX TT UM VC VG VI YT".Split(
            ' '
        ),
    ];

    // The country code and CUSIP issuer number of a CUSIP-numbered ISIN whose embedded CINS check digit holds; else null.
    public static string CusipIssuerNumber(string isin)
    {
        if (
            !IsValidIsin(isin)
            || !CusipNumberedJurisdictions.Contains(isin[..2])
            || !char.IsAsciiDigit(isin[10])
        )
            return null;
        var sum = 0;
        for (var index = 0; index < 8; index++)
        {
            var character = isin[2 + index];
            var value = character <= '9' ? character - '0' : character - 'A' + 10;
            if (index % 2 == 1)
                value *= 2;
            sum += value / 10 + value % 10;
        }
        return (10 - sum % 10) % 10 == isin[10] - '0' ? isin[..8] : null;
    }

    // ISO 17442 uses the ISO 7064 MOD 97-10 check.
    public static bool IsValidLei(string value)
    {
        if (value?.Length != 20 || value[^1] is < '0' or > '9' || value[^2] is < '0' or > '9')
            return false;
        var remainder = 0;
        foreach (var character in value)
        {
            if (character is >= '0' and <= '9')
                remainder = (remainder * 10 + character - '0') % 97;
            else if (character is >= 'A' and <= 'Z')
                remainder = (remainder * 100 + character - 'A' + 10) % 97;
            else
                return false;
        }
        return remainder == 1;
    }
}
