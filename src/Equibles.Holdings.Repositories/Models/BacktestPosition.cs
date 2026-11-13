namespace Equibles.Holdings.Repositories.Models;

public class BacktestPosition
{
    public Guid CommonStockId { get; set; }

    /// <summary>
    /// Exact listed ticker for a sibling security; null means the issuer's primary listing.
    /// </summary>
    public string ListedTicker { get; set; }

    public long Shares { get; set; }

    public long Value { get; set; }

    public bool IsOption { get; set; }

    /// <summary>
    /// Set by the price loader when the exact listing has no usable close on the day this snapshot
    /// is bought (a delisted company the price source no longer serves, or a line priced only from
    /// a later date). The clone cannot hold it, so it is left out of the weights and counted as
    /// untracked in <see cref="BacktestCoverage"/>, exactly like an option leg.
    /// </summary>
    public bool Unpriced { get; set; }

    /// <summary>A long position the clone can buy: not an option, valued, and priced.</summary>
    public bool IsCloneable => !IsOption && !Unpriced && Value > 0;
}
