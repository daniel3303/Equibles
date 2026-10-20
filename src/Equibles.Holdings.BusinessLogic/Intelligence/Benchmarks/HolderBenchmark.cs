namespace Equibles.Holdings.BusinessLogic.Intelligence.Benchmarks;

/// <summary>
/// One benchmark's weights as of a date. A name resolved to a tracked issuer weighs under the
/// issuer's id; an unresolved name keeps its weight under a key no issuer has, so it counts as a
/// benchmark name every filer lacks.
/// </summary>
public sealed record HolderBenchmark
{
    /// <summary>A <see cref="HolderBenchmarkCatalog"/> key.</summary>
    public string Key { get; init; }

    public DateOnly AsOf { get; init; }

    /// <summary>Each name's weight on any positive scale, such as its market value.</summary>
    public IReadOnlyDictionary<Guid, decimal> Weights { get; init; } =
        new Dictionary<Guid, decimal>();

    /// <summary>The weight resolved to tracked issuers, from 0 to 1.</summary>
    public decimal Coverage { get; init; } = 1m;

    /// <summary>Breaks a tie between two sources of one key at the same distance; lower wins.</summary>
    public int Priority { get; init; }

    /// <summary>Where the weights were read, for logs.</summary>
    public string Source { get; init; }
}
