using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.BusinessLogic.Contracts;

/// <summary>
/// Optional source of the investment strategy a holder states in a document it filed. A deployment
/// with such a source registers an implementation; nothing is registered by default, and the
/// institution summary then shows no stated strategy. A stated strategy is the firm's own
/// description, so it never decides the holder's entity type or its measured style bands.
/// </summary>
public interface IHolderStrategyStatements
{
    /// <summary>
    /// The holder's current stated strategies, one per filed document that states any (a firm can
    /// file several brochures), newest first; empty when none is on file.
    /// </summary>
    Task<IReadOnlyList<HolderStatedStrategy>> Get(
        InstitutionalHolder holder,
        CancellationToken cancellationToken = default
    );
}
