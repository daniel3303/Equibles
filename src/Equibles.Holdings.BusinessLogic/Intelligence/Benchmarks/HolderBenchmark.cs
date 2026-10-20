namespace Equibles.Holdings.BusinessLogic.Intelligence.Benchmarks;

/// <summary>
/// One benchmark's weights as of a date, over the names resolved to tracked issuers only: a 13F
/// book can never hold an unresolved name, so keeping one would floor every book's active share at
/// its weight. <see cref="Coverage"/> records how much of the benchmark resolved.
/// </summary>
public sealed record HolderBenchmark
{
    /// <summary>
    /// The least coverage a benchmark needs to be the closest one and feed the index-like band.
    /// Renormalizing over the resolved names moves the benchmark by at most one minus its
    /// coverage in active-share terms, so at 90% no book's active share is off by more than 10
    /// points, half the index-like threshold; below it the stored row is kept but never decides.
    /// </summary>
    public const decimal DefaultMinimumCoverage = 0.90m;

    /// <summary>A <see cref="HolderBenchmarkCatalog"/> key.</summary>
    public string Key { get; init; }

    public DateOnly AsOf { get; init; }

    /// <summary>Each resolved issuer's weight on any positive scale, such as its market value.</summary>
    public IReadOnlyDictionary<Guid, decimal> Weights { get; init; } =
        new Dictionary<Guid, decimal>();

    /// <summary>The weight resolved to tracked issuers, from 0 to 1.</summary>
    public decimal Coverage { get; init; } = 1m;

    /// <summary>Breaks a tie between two sources of one key at the same distance; lower wins.</summary>
    public int Priority { get; init; }

    /// <summary>Where the weights were read, for logs.</summary>
    public string Source { get; init; }
}
