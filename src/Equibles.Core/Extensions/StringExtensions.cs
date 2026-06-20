namespace Equibles.Core.Extensions;

public static class StringExtensions
{
    /// <summary>
    /// Caps <paramref name="value"/> at <paramref name="maxLength"/> UTF-16 units without splitting
    /// a surrogate pair. External free text (filings, exception messages, award descriptions, file
    /// names) can contain non-BMP characters; a raw <c>[..maxLength]</c> slice can leave an orphan
    /// high surrogate that corrupts the PostgreSQL UTF-8 round-trip (error 22001) and any JSON
    /// serialization. <c>null</c> and values already within the bound pass through unchanged.
    /// </summary>
    public static string TruncateToFit(this string value, int maxLength)
    {
        if (value == null || value.Length <= maxLength)
            return value;

        // Back off one unit when the cap lands on the high half of a surrogate pair so the kept
        // prefix never ends in a lone surrogate.
        var end =
            maxLength > 0 && char.IsHighSurrogate(value[maxLength - 1]) ? maxLength - 1 : maxLength;
        return value[..end];
    }
}
