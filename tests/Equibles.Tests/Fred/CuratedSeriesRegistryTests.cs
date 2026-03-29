using Equibles.Fred.Data.Models;
using Equibles.Fred.HostedService.Services;

namespace Equibles.Tests.Fred;

public class CuratedSeriesRegistryTests {
    [Fact]
    public void GetAll_ReturnsNonEmptyList() {
        CuratedSeriesRegistry.Series.Should().NotBeEmpty();
    }

    [Fact]
    public void AllSeries_HaveNonEmptySeriesId() {
        CuratedSeriesRegistry.Series
            .Should().AllSatisfy(s => s.SeriesId.Should().NotBeNullOrWhiteSpace());
    }

    [Fact]
    public void AllSeries_HaveValidCategory() {
        var validCategories = Enum.GetValues<FredSeriesCategory>();

        CuratedSeriesRegistry.Series
            .Should().AllSatisfy(s => validCategories.Should().Contain(s.Category));
    }

    [Fact]
    public void AllSeries_HaveNoDuplicateSeriesIds() {
        var ids = CuratedSeriesRegistry.Series.Select(s => s.SeriesId).ToList();
        ids.Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData("GDP")]
    [InlineData("UNRATE")]
    [InlineData("FEDFUNDS")]
    [InlineData("CPIAUCSL")]
    [InlineData("SP500")]
    [InlineData("VIXCLS")]
    [InlineData("MORTGAGE30US")]
    [InlineData("M2SL")]
    [InlineData("ICSA")]
    [InlineData("HOUST")]
    public void Series_ContainsExpectedKeySeriesId(string expectedSeriesId) {
        CuratedSeriesRegistry.Series
            .Should().Contain(s => s.SeriesId == expectedSeriesId);
    }

    [Theory]
    [InlineData("GDP", FredSeriesCategory.GdpAndOutput)]
    [InlineData("GDPC1", FredSeriesCategory.GdpAndOutput)]
    [InlineData("UNRATE", FredSeriesCategory.Employment)]
    [InlineData("PAYEMS", FredSeriesCategory.Employment)]
    [InlineData("FEDFUNDS", FredSeriesCategory.InterestRates)]
    [InlineData("EFFR", FredSeriesCategory.InterestRates)]
    [InlineData("CPIAUCSL", FredSeriesCategory.Inflation)]
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
    public void WellKnownSeries_HasExpectedCategory(
        string seriesId, FredSeriesCategory expectedCategory) {
        var series = CuratedSeriesRegistry.Series.Single(s => s.SeriesId == seriesId);
        series.Category.Should().Be(expectedCategory);
    }

    [Fact]
    public void AllCategories_HaveAtLeastOneSeries() {
        var allCategories = Enum.GetValues<FredSeriesCategory>();
        var representedCategories = CuratedSeriesRegistry.Series
            .Select(s => s.Category)
            .Distinct()
            .ToHashSet();

        representedCategories.Should().BeEquivalentTo(allCategories);
    }

    [Fact]
    public void InterestRates_ContainsMultipleSeries() {
        CuratedSeriesRegistry.Series
            .Where(s => s.Category == FredSeriesCategory.InterestRates)
            .Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void Series_IsReadOnly() {
        CuratedSeriesRegistry.Series.Should().BeAssignableTo<IReadOnlyList<CuratedSeries>>();
    }
}
