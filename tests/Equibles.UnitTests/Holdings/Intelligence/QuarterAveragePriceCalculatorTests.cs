using Equibles.Holdings.BusinessLogic.Intelligence;
using Equibles.Holdings.BusinessLogic.Intelligence.Models;

namespace Equibles.UnitTests.Holdings.Intelligence;

public class QuarterAveragePriceCalculatorTests
{
    private static readonly DateOnly QuarterEnd = new(2024, 6, 30);

    private static readonly QuarterPriceSeries Series = new()
    {
        AverageClose = 8m,
        LastClose = 10m,
        LastSessionDate = new DateOnly(2024, 6, 28),
        Sessions = 62,
        ShareCountFactor = 1m,
        FiledPricePerShare = 10m,
    };

    [Fact]
    public void AveragePrice_IsTheMeanCloseWhenTheSeriesMatchesTheFiledPrice()
    {
        QuarterAveragePriceCalculator.AveragePrice(Series, QuarterEnd).Should().Be(8m);
    }

    [Fact]
    public void AveragePrice_PutsALaterSplitsRestatedClosesBackOnTheFilingBasis()
    {
        var restated = Series with { AverageClose = 4m, LastClose = 5m, ShareCountFactor = 2m };

        QuarterAveragePriceCalculator.AveragePrice(restated, QuarterEnd).Should().Be(8m);
    }

    [Theory]
    [InlineData(12.5, true)]
    [InlineData(12.6, false)]
    [InlineData(8, true)]
    [InlineData(7.9, false)]
    public void AveragePrice_AcceptsAQuarterEndCloseWithinAQuarterOfTheFiledPrice(
        double lastClose,
        bool accepted
    )
    {
        var series = Series with { LastClose = (decimal)lastClose };

        QuarterAveragePriceCalculator
            .AveragePrice(series, QuarterEnd)
            .HasValue.Should()
            .Be(accepted);
    }

    [Fact]
    public void AveragePrice_RefusesALaterSplitSmallEnoughThatBothBasesAgree()
    {
        var fiveForFour = Series with
        {
            AverageClose = 6.4m,
            LastClose = 8m,
            ShareCountFactor = 1.25m,
        };

        QuarterAveragePriceCalculator.AveragePrice(fiveForFour, QuarterEnd).Should().BeNull();
    }

    [Theory]
    [InlineData("factor")]
    [InlineData("filed")]
    [InlineData("last")]
    [InlineData("average")]
    public void AveragePrice_RefusesAZeroInput(string input)
    {
        var series = input switch
        {
            "factor" => Series with { ShareCountFactor = 0m },
            "filed" => Series with { FiledPricePerShare = 0m },
            "last" => Series with { LastClose = 0m },
            _ => Series with { AverageClose = 0m },
        };

        QuarterAveragePriceCalculator.AveragePrice(series, QuarterEnd).Should().BeNull();
    }

    [Fact]
    public void AveragePrice_RefusesASeriesStillOnThePreSplitBasis()
    {
        var unrestated = Series with { ShareCountFactor = 2m };

        QuarterAveragePriceCalculator.AveragePrice(unrestated, QuarterEnd).Should().BeNull();
    }

    [Fact]
    public void AveragePrice_RefusesTooFewSessions()
    {
        QuarterAveragePriceCalculator
            .AveragePrice(Series with { Sessions = 19 }, QuarterEnd)
            .Should()
            .BeNull();
        QuarterAveragePriceCalculator
            .AveragePrice(Series with { Sessions = 20 }, QuarterEnd)
            .Should()
            .Be(8m);
    }

    [Fact]
    public void AveragePrice_RefusesASeriesThatStoppedTradingBeforeTheLastWeek()
    {
        QuarterAveragePriceCalculator
            .AveragePrice(Series with { LastSessionDate = new DateOnly(2024, 6, 22) }, QuarterEnd)
            .Should()
            .BeNull();
        QuarterAveragePriceCalculator
            .AveragePrice(Series with { LastSessionDate = new DateOnly(2024, 6, 23) }, QuarterEnd)
            .Should()
            .Be(8m);
    }

    [Fact]
    public void AveragePrice_RefusesASplitInsideTheQuarter()
    {
        QuarterAveragePriceCalculator
            .AveragePrice(Series with { SplitInsideQuarter = true }, QuarterEnd)
            .Should()
            .BeNull();
    }

    [Fact]
    public void AveragePrice_RefusesAnUnresolvedSplitBasis()
    {
        QuarterAveragePriceCalculator
            .AveragePrice(Series with { ShareCountFactor = null }, QuarterEnd)
            .Should()
            .BeNull();
    }

    [Fact]
    public void AveragePrice_RefusesASecurityNoFilerValued()
    {
        QuarterAveragePriceCalculator
            .AveragePrice(Series with { FiledPricePerShare = null }, QuarterEnd)
            .Should()
            .BeNull();
    }
}
