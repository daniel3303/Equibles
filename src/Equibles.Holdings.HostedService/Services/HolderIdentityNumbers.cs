namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// One rule for a holder's identity numbers read from a cover page: the filer's own number wins;
/// a blank keeps the stored number, unless the stored number is one this cover lists for another
/// manager, which the cover parser once took when the filer left its own element blank.
/// </summary>
internal static class HolderIdentityNumbers
{
    internal static string Resolve(
        string coverValue,
        string storedValue,
        IEnumerable<string> otherManagerValues
    )
    {
        var own = HoldingsParsingHelper.NormalizeIdentifier(coverValue);
        if (own != null)
            return own;

        var stored = HoldingsParsingHelper.NormalizeIdentifier(storedValue);
        if (stored == null)
            return null;

        return otherManagerValues.Any(value =>
            string.Equals(
                HoldingsParsingHelper.NormalizeIdentifier(value),
                stored,
                StringComparison.OrdinalIgnoreCase
            )
        )
            ? null
            : stored;
    }
}
