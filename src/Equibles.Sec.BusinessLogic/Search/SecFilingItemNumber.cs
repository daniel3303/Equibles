namespace Equibles.Sec.BusinessLogic.Search;

/// <summary>
/// Validates SEC current-report item numbers used by filing-list filters.
/// </summary>
public static class SecFilingItemNumber
{
    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var item = value.Trim();
        return
            item.Length == 4
            && item[0] is >= '1' and <= '9'
            && item[1] == '.'
            && char.IsAsciiDigit(item[2])
            && char.IsAsciiDigit(item[3])
            ? item
            : null;
    }

    public static IReadOnlyList<string> ParseStored(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            );
}
