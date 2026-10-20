namespace Equibles.Holdings.HostedService.Services.Benchmarks;

/// <summary>
/// An index-tracking fund whose N-PORT holdings weight one published index, identified by its SEC
/// series id, or by its trust's CIK for a unit investment trust that files without one.
/// </summary>
public class HolderBenchmarkTracker
{
    /// <summary>A <c>HolderBenchmarkCatalog</c> key, such as <c>sp-500</c>.</summary>
    public string BenchmarkKey { get; set; }

    /// <summary>The SEC series id, such as <c>S000004310</c>.</summary>
    public string SeriesId { get; set; }

    /// <summary>The trust's SEC CIK; matches only its filings that carry no series id.</summary>
    public string Cik { get; set; }
}
