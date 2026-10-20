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
}
