using Equibles.Holdings.BusinessLogic.Ownership.Models;

namespace Equibles.Holdings.BusinessLogic.Ownership;

/// <summary>
/// One disclosure lane of the unified holder list. Each lane reads only the scope's stock, gives
/// one row per holder with shares restated onto today's split basis, and compares a holder only
/// with its own previous filing in the same lane. Lanes register with <c>TryAddEnumerable</c>, so a
/// deployment adds its own (non-US regulators, licensed fund data) beside these.
/// </summary>
public interface IHolderSourceContributor
{
    Task<IReadOnlyList<StockHolderRow>> Read(
        StockOwnershipScope scope,
        CancellationToken cancellationToken = default
    );
}
