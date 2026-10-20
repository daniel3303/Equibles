using Equibles.Holdings.BusinessLogic.Intelligence.Benchmarks;
using Equibles.Holdings.HostedService.Services.Benchmarks;

namespace Equibles.UnitTests.Holdings.Intelligence;

public class HolderBenchmarkSelectionTests
{
    private static readonly DateOnly Quarter = new(2026, 6, 30);

    [Fact]
    public void Choose_TakesTheSourceNearestTheQuarterEndPerKey()
    {
        var chosen = HolderBenchmarkSelection.Choose(
            [
                Benchmark(HolderBenchmarkCatalog.Russell1000, new DateOnly(2026, 5, 31), "fiscal"),
                Benchmark(HolderBenchmarkCatalog.Russell1000, Quarter, "calendar"),
                Benchmark(HolderBenchmarkCatalog.Aggregate, Quarter, "13F"),
            ],
            Quarter
        );

        chosen
            .Select(benchmark => (benchmark.Key, benchmark.Source))
            .Should()
            .Equal(
                (HolderBenchmarkCatalog.Russell1000, "calendar"),
                (HolderBenchmarkCatalog.Aggregate, "13F")
            );
    }

    [Fact]
    public void Choose_AtEqualDistance_PrefersOnOrBeforeThenPriority()
    {
        var before = Benchmark(HolderBenchmarkCatalog.Sp500, Quarter.AddDays(-10), "before", 100);
        var after = Benchmark(HolderBenchmarkCatalog.Sp500, Quarter.AddDays(10), "after", 0);
        var constituents = Benchmark(HolderBenchmarkCatalog.Sp500, Quarter.AddDays(-10), "list", 0);

        HolderBenchmarkSelection
            .Choose([after, before], Quarter)
            .Single()
            .Source.Should()
            .Be("before");
        HolderBenchmarkSelection
            .Choose([after, before, constituents], Quarter)
            .Single()
            .Source.Should()
            .Be("list");
    }

    [Fact]
    public void Choose_DropsKeysOutsideTheCatalogAndEmptyWeights()
    {
        var chosen = HolderBenchmarkSelection.Choose(
            [
                Benchmark("msci-world", Quarter, "unknown"),
                Benchmark(HolderBenchmarkCatalog.Nasdaq100, Quarter, "empty") with
                {
                    Weights = new Dictionary<Guid, decimal> { [Guid.NewGuid()] = 0 },
                },
            ],
            Quarter
        );

        chosen.Should().BeEmpty();
    }

    [Theory]
    [InlineData("037833100", null, "037833100")]
    [InlineData("037833100 ", "US0378331005", "037833100")]
    [InlineData(null, "US0378331005", "037833100")]
    [InlineData("000000000", "CA0679011084", "067901108")]
    [InlineData("000000000", "IE00BLP1HW54", null)]
    [InlineData(null, null, null)]
    public void CusipOf_ReadsTheFiledCusipOrTheOneInANorthAmericanIsin(
        string cusip,
        string isin,
        string expected
    )
    {
        NportSeriesHolderBenchmarkProvider.CusipOf(cusip, isin).Should().Be(expected);
    }

    private static HolderBenchmark Benchmark(
        string key,
        DateOnly asOf,
        string source,
        int priority = 0
    ) =>
        new()
        {
            Key = key,
            AsOf = asOf,
            Source = source,
            Priority = priority,
            Weights = new Dictionary<Guid, decimal> { [Guid.NewGuid()] = 1 },
        };
}
