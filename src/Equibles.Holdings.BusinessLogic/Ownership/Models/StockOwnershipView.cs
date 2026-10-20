namespace Equibles.Holdings.BusinessLogic.Ownership.Models;

/// <summary>How the unified holder list is arranged.</summary>
public enum StockOwnershipView
{
    /// <summary>One row per holder from its freshest filing, related disclosures nested under it.</summary>
    Default = 0,

    /// <summary>Every filing from every source, stale ones flagged.</summary>
    All = 1,

    /// <summary>The summed holders grouped under their ultimate parent.</summary>
    Ultimate = 2,
}
