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
    /// The newest row of the filing that reports a trade, or null when the filing reports
    /// positions only. A Form 4 lists untouched holdings after its trades, so the anchor is
    /// often a holding line; readers that name the insider's last trade read this instead.
    /// </summary>
    public InsiderTransaction LastTransaction { get; init; }

    /// <summary>The row readers show as the insider's latest activity: the last trade, else the anchor.</summary>
    public InsiderTransaction LatestActivity => LastTransaction ?? Anchor;

    /// <summary>
    /// True when the filing's indirect rows do not show which balances belong to one vehicle
    /// (<see cref="InsiderPositionClosings.IndirectAmbiguous"/>), so its indirect total cannot be
    /// compared with another filing's. One vehicle traded several times is not ambiguous.
    /// </summary>
    public bool IndirectBalancesAmbiguous { get; init; }
}
