using Equibles.Yahoo.Repositories;
using FluentAssertions;

namespace Equibles.UnitTests.Web;

public class TechnicalIndicatorServiceBollingerBandsTests
{
    [Fact]
    public void ComputeBollingerBands_NullPadsWarmupAndAlignsToInput()
    {
        var prices = Enumerable.Range(1, 25).Select(i => (decimal)i).ToList();

        var (middle, upper, lower) = TechnicalIndicatorService.ComputeBollingerBands(
            prices,
            period: 20
        );

        middle.Should().HaveCount(prices.Count);
        upper.Should().HaveCount(prices.Count);
        lower.Should().HaveCount(prices.Count);

        // First (period - 1) bars are warm-up and stay null on every band.
        middle.Take(19).Should().AllSatisfy(v => v.Should().BeNull());
        upper.Take(19).Should().AllSatisfy(v => v.Should().BeNull());
        lower.Take(19).Should().AllSatisfy(v => v.Should().BeNull());
        middle.Skip(19).Should().AllSatisfy(v => v.Should().NotBeNull());
    }

    [Fact]
    public void ComputeBollingerBands_FlatPrices_BandsCollapseToTheMiddle()
    {
        var prices = Enumerable.Repeat(50m, 20).ToList();

        var (middle, upper, lower) = TechnicalIndicatorService.ComputeBollingerBands(
            prices,
            period: 20
        );

        // Zero variance ⇒ zero deviation ⇒ all three bands equal the mean.
        middle[^1].Should().Be(50m);
        upper[^1].Should().Be(50m);
        lower[^1].Should().Be(50m);
    }

    [Fact]
    public void ComputeBollingerBands_KnownWindow_MatchesPopulationStdDev()
    {
        // A single full window of 1..5 so the math is hand-checkable.
        // mean = 3; population variance = ((-2)²+(-1)²+0²+1²+2²)/5 = 10/5 = 2;
        // population std = √2 ≈ 1.4142; with stdDev = 2 the offset ≈ 2.8284.
        var prices = new List<decimal> { 1m, 2m, 3m, 4m, 5m };

        var (middle, upper, lower) = TechnicalIndicatorService.ComputeBollingerBands(
            prices,
            period: 5,
            stdDev: 2m
        );

        middle[^1].Should().Be(3m);
        upper[^1].Should().BeApproximately(5.8284m, 0.0001m);
        lower[^1].Should().BeApproximately(0.1716m, 0.0001m);
    }

    [Fact]
    public void ComputeBollingerBands_PeriodLargerThanInput_AllNull()
    {
        var prices = new List<decimal> { 1m, 2m, 3m };

        var (middle, upper, lower) = TechnicalIndicatorService.ComputeBollingerBands(
            prices,
            period: 20
        );

        middle.Should().AllSatisfy(v => v.Should().BeNull());
        upper.Should().AllSatisfy(v => v.Should().BeNull());
        lower.Should().AllSatisfy(v => v.Should().BeNull());
    }

    [Fact]
    public void ComputeBollingerBands_EmptyInput_ReturnsEmptyLists()
    {
        var (middle, upper, lower) = TechnicalIndicatorService.ComputeBollingerBands([]);

        middle.Should().BeEmpty();
        upper.Should().BeEmpty();
        lower.Should().BeEmpty();
    }
}
