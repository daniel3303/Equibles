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
