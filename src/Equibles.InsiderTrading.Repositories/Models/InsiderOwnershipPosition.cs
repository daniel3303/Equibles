using Equibles.InsiderTrading.Data.Models;

namespace Equibles.InsiderTrading.Repositories.Models;

/// <summary>
/// One insider's position from one filing: the summed closing balances of its direct and indirect
/// buckets on today's split basis, and the filing's last row as the anchor.
/// </summary>
public sealed record InsiderOwnershipPosition(
    InsiderTransaction Anchor,
    long DirectShares,
    long IndirectShares
)
{
    public long TotalShares => DirectShares + IndirectShares;

    /// <summary>
    /// True when an indirect bucket states several different balances. A filing keeps no vehicle
    /// identity, so they may be separate trusts, and the closing balance cannot be compared
    /// with another filing's.
    /// </summary>
    public bool IndirectBalancesAmbiguous { get; init; }
}
