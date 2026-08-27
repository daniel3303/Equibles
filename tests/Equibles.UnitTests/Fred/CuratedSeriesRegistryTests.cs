using Equibles.Fred.Data.Models;
using Equibles.Fred.HostedService.Services;

namespace Equibles.UnitTests.Fred;

public class CuratedSeriesRegistryTests
{
    [Fact]
    public void GetAll_ReturnsNonEmptyList()
    {
        CuratedSeriesRegistry.Series.Should().NotBeEmpty();
    }

    [Fact]
    public void AllSeries_HaveNonEmptySeriesId()
    {
        CuratedSeriesRegistry
            .Series.Should()
            .AllSatisfy(s => s.SeriesId.Should().NotBeNullOrWhiteSpace());
    }

    [Fact]
    public void AllSeries_HaveValidCategory()
    {
        var validCategories = Enum.GetValues<FredSeriesCategory>();

        CuratedSeriesRegistry
            .Series.Should()
            .AllSatisfy(s => validCategories.Should().Contain(s.Category));
    }

    [Fact]
    public void AllSeries_HaveNoDuplicateSeriesIds()
    {
        var ids = CuratedSeriesRegistry.Series.Select(s => s.SeriesId).ToList();
        ids.Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData("GDP")]
    [InlineData("UNRATE")]
    [InlineData("FEDFUNDS")]
    [InlineData("CPIAUCSL")]
    [InlineData("PPIFIS")]
    [InlineData("SP500")]
    [InlineData("VIXCLS")]
    [InlineData("MORTGAGE30US")]
    [InlineData("DGS2")]
    [InlineData("DGS10")]
    [InlineData("DGS30")]
    [InlineData("M2SL")]
    [InlineData("ICSA")]
    [InlineData("HOUST")]
    [InlineData("DEXTAUS")]
    public void Series_ContainsExpectedKeySeriesId(string expectedSeriesId)
    {
        CuratedSeriesRegistry.Series.Should().Contain(s => s.SeriesId == expectedSeriesId);
    }

    [Theory]
    [InlineData("GDP", FredSeriesCategory.GdpAndOutput)]
    [InlineData("GDPC1", FredSeriesCategory.GdpAndOutput)]
    [InlineData("UNRATE", FredSeriesCategory.Employment)]
    [InlineData("PAYEMS", FredSeriesCategory.Employment)]
    [InlineData("FEDFUNDS", FredSeriesCategory.InterestRates)]
    [InlineData("EFFR", FredSeriesCategory.InterestRates)]
    [InlineData("CPIAUCSL", FredSeriesCategory.Inflation)]
    [InlineData("PPIFIS", FredSeriesCategory.Inflation)]
    [InlineData("T10YIE", FredSeriesCategory.Inflation)]
    [InlineData("SP500", FredSeriesCategory.Market)]
    [InlineData("VIXCLS", FredSeriesCategory.Market)]
    [InlineData("MORTGAGE30US", FredSeriesCategory.Housing)]
    [InlineData("HOUST", FredSeriesCategory.Housing)]
    [InlineData("T10Y2Y", FredSeriesCategory.YieldSpreads)]
    [InlineData("BAMLH0A0HYM2", FredSeriesCategory.CorporateBondSpreads)]
    [InlineData("M2SL", FredSeriesCategory.MoneySupply)]
    [InlineData("UMCSENT", FredSeriesCategory.Sentiment)]
    [InlineData("DTWEXBGS", FredSeriesCategory.ExchangeRates)]
    [InlineData("DEXTAUS", FredSeriesCategory.ExchangeRates)]
    public void WellKnownSeries_HasExpectedCategory(
        string seriesId,
        FredSeriesCategory expectedCategory
    )
    {
        var series = CuratedSeriesRegistry.Series.Single(s => s.SeriesId == seriesId);
        series.Category.Should().Be(expectedCategory);
    }

    [Fact]
    public void Series_TracksStlfsi4NotTheDiscontinuedStlfsi2()
    {
        // STLFSI2 was discontinued by FRED (frozen at 2022-01-07); its stale value
        // polluted the "current macro conditions" snapshot. STLFSI4 supersedes it.
        CuratedSeriesRegistry
            .Series.Should()
            .Contain(s => s.SeriesId == "STLFSI4" && s.Category == FredSeriesCategory.Market);
        CuratedSeriesRegistry.Series.Should().NotContain(s => s.SeriesId == "STLFSI2");
    }

    [Fact]
    public void Series_ExcludesTheSeriesFredHasStoppedUpdating()
    {
        // The rule STLFSI2 taught, written so it survives the next curation pass. Both of these
        // clear the popularity floor the list is chosen against, and both would render a
        // current-looking page over a number that is not current: USSLIND stopped updating in
        // April 2020, and FPCPITOTLZGUSA is an annual World Bank restatement of US CPI that lags
        // CPIAUCSL by a year. Popularity alone must never be enough to get a series in.
        CuratedSeriesRegistry.Series.Should().NotContain(s => s.SeriesId == "USSLIND");
        CuratedSeriesRegistry.Series.Should().NotContain(s => s.SeriesId == "FPCPITOTLZGUSA");
    }

    [Fact]
    public void InterestRates_CoverTheWholeTreasuryCurve()
    {
        // A yield curve with holes cannot answer a question about its own shape: the front end
        // is where policy expectations show up, and the list used to start at the 2-year.
        var maturities = CuratedSeriesRegistry
            .Series.Where(s => s.Category == FredSeriesCategory.InterestRates)
            .Select(s => s.SeriesId)
            .ToList();

        maturities
            .Should()
            .Contain([
                "DGS1MO",
                "DGS3MO",
                "DGS6MO",
                "DGS1",
                "DGS2",
                "DGS3",
                "DGS5",
                "DGS7",
                "DGS10",
                "DGS20",
                "DGS30",
            ]);
    }

    [Fact]
    public void CorporateBondSpreads_SeparateTheRatingBuckets()
    {
        // BAMLH0A0HYM2 is the aggregate. Without BB, B and CCC underneath it, a widening cannot
        // be told apart from a rotation within high yield.
        CuratedSeriesRegistry
            .Series.Where(s => s.Category == FredSeriesCategory.CorporateBondSpreads)
            .Select(s => s.SeriesId)
            .Should()
            .Contain(["BAMLH0A1HYBB", "BAMLH0A2HYB", "BAMLH0A3HYC"]);
    }

    [Fact]
    public void AllCategories_HaveAtLeastOneSeries()
    {
        var allCategories = Enum.GetValues<FredSeriesCategory>();
        var representedCategories = CuratedSeriesRegistry
            .Series.Select(s => s.Category)
            .Distinct()
            .ToHashSet();

        representedCategories.Should().BeEquivalentTo(allCategories);
    }

    [Fact]
    public void InterestRates_ContainsMultipleSeries()
    {
        CuratedSeriesRegistry
            .Series.Where(s => s.Category == FredSeriesCategory.InterestRates)
            .Should()
            .HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void Series_IsReadOnly()
    {
        CuratedSeriesRegistry.Series.Should().BeAssignableTo<IReadOnlyList<CuratedSeries>>();
    }
}
