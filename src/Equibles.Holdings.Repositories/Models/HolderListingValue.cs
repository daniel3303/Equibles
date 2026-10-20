namespace Equibles.Holdings.Repositories.Models;

/// <summary>One filer's common-share position in one listed security at a quarter end, summed.</summary>
public sealed class HolderListingValue
{
    public Guid InstitutionalHolderId { get; init; }
    public Guid EquityIssuerId { get; init; }

    /// <summary>Null for the issuer's primary listing.</summary>
    public string ListedTicker { get; init; }

    public long Shares { get; init; }
    public long Value { get; init; }
}
