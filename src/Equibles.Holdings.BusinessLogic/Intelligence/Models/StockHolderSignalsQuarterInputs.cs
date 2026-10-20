using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.BusinessLogic.Intelligence.Models;

/// <summary>The quarter-wide inputs the stock signals read besides the filers' books.</summary>
public sealed record StockHolderSignalsQuarterInputs
{
    /// <summary>
    /// Each issuer's cover-page count on the quarter's basis, with its date and basis; an issuer
    /// with none is absent.
    /// </summary>
    public IReadOnlyDictionary<
        Guid,
        (long Shares, DateOnly AsOf, SharesOutstandingBasis Basis)
    > SharesOutstanding { get; init; } =
        new Dictionary<Guid, (long, DateOnly, SharesOutstandingBasis)>();

    /// <summary>Insiders' shares per issuer, only where a consolidated count can divide them.</summary>
    public IReadOnlyDictionary<Guid, long> InsiderShares { get; init; } =
        new Dictionary<Guid, long>();

    public IReadOnlyDictionary<
        HolderSecurityKey,
        Schedule13DGCounts
    > BeneficialOwnership { get; init; } = new Dictionary<HolderSecurityKey, Schedule13DGCounts>();

    /// <summary>True when top-scored managers' net buying is measured for this quarter.</summary>
    public bool MeasuresTopScored { get; init; }
}
