using Equibles.Holdings.BusinessLogic.Intelligence.Benchmarks;

namespace Equibles.Holdings.HostedService.Services.Benchmarks;

/// <summary>
/// The index-tracking funds the N-PORT benchmark reads, bound from <see cref="SectionName"/>; an
/// empty list reads <see cref="Defaults"/>. Earlier entries of one index win a tie on report date.
/// </summary>
public class HolderBenchmarkOptions
{
    public const string SectionName = "HolderBenchmarks";

    public List<HolderBenchmarkTracker> Trackers { get; set; } = [];

    public IReadOnlyList<HolderBenchmarkTracker> EffectiveTrackers =>
        Trackers is { Count: > 0 } ? Trackers : Defaults;

    /// <summary>
    /// Each fund's index is the one its own prospectus names (summary prospectus or 485BPOS
    /// investment objective, read 2026-10-20), and each series reports as an index fund on N-CEN;
    /// never inferred from a fund's name.
    /// </summary>
    public static IReadOnlyList<HolderBenchmarkTracker> Defaults { get; } =
    [
        // iShares Core S&P 500 ETF, Vanguard 500 Index Fund, SPDR S&P 500 ETF Trust (UIT).
        Series(HolderBenchmarkCatalog.Sp500, "S000004310"),
        Series(HolderBenchmarkCatalog.Sp500, "S000002839"),
        Trust(HolderBenchmarkCatalog.Sp500, "884394"),
        // iShares Core S&P Mid-Cap ETF, Vanguard S&P Mid-Cap 400 Index Fund, SPDR S&P MidCap 400
        // ETF Trust (UIT).
        Series(HolderBenchmarkCatalog.Sp400, "S000004307"),
        Series(HolderBenchmarkCatalog.Sp400, "S000030014"),
        Trust(HolderBenchmarkCatalog.Sp400, "936958"),
        // iShares Core S&P Small-Cap ETF, Vanguard S&P Small-Cap 600 Index Fund.
        Series(HolderBenchmarkCatalog.Sp600, "S000004313"),
        Series(HolderBenchmarkCatalog.Sp600, "S000030017"),
        // iShares Russell 1000 ETF, Vanguard Russell 1000 Index Fund.
        Series(HolderBenchmarkCatalog.Russell1000, "S000004347"),
        Series(HolderBenchmarkCatalog.Russell1000, "S000030000"),
        // iShares Russell 2000 ETF, Vanguard Russell 2000 Index Fund.
        Series(HolderBenchmarkCatalog.Russell2000, "S000004344"),
        Series(HolderBenchmarkCatalog.Russell2000, "S000030003"),
        // Invesco QQQ Trust, Series 1 (a series since 2025-12, a UIT before), Invesco NASDAQ 100 ETF.
        Series(HolderBenchmarkCatalog.Nasdaq100, "S000101292"),
        Series(HolderBenchmarkCatalog.Nasdaq100, "S000069448"),
        Trust(HolderBenchmarkCatalog.Nasdaq100, "1067839"),
        // SPDR Dow Jones Industrial Average ETF Trust (UIT); no series-id fund replicates the index.
        Trust(HolderBenchmarkCatalog.DowJones, "1041130"),
    ];

    private static HolderBenchmarkTracker Series(string key, string seriesId) =>
        new() { BenchmarkKey = key, SeriesId = seriesId };

    private static HolderBenchmarkTracker Trust(string key, string cik) =>
        new() { BenchmarkKey = key, Cik = cik };
}
