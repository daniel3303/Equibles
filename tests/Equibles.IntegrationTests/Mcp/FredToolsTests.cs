using Equibles.Fred.Data.Models;
using Equibles.Fred.Mcp.Tools;
using Equibles.Fred.Repositories;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

[Collection(ParadeDbCollection.Name)]
public class FredToolsTests : ParadeDbMcpTestBase {
    private FredSeries _fedFundsSeries;
    private FredSeries _cpiSeries;
    private FredSeries _gdpSeries;

    public FredToolsTests(ParadeDbFixture fixture) : base(fixture) { }

    private FredTools Sut() => new(
        new FredSeriesRepository(DbContext),
        new FredObservationRepository(DbContext),
        ErrorManager,
        NullLogger<FredTools>());

    public override async Task InitializeAsync() {
        await base.InitializeAsync();

        _fedFundsSeries = new FredSeries {
            SeriesId = "FEDFUNDS", Title = "Federal Funds Effective Rate",
            Category = FredSeriesCategory.InterestRates, Frequency = "Monthly",
            Units = "Percent", SeasonalAdjustment = "Not Seasonally Adjusted",
        };
        _cpiSeries = new FredSeries {
            SeriesId = "CPIAUCSL", Title = "Consumer Price Index for All Urban Consumers",
            Category = FredSeriesCategory.Inflation, Frequency = "Monthly",
            Units = "Index 1982-1984=100", SeasonalAdjustment = "Seasonally Adjusted",
        };
        _gdpSeries = new FredSeries {
            SeriesId = "GDP", Title = "Gross Domestic Product",
            Category = FredSeriesCategory.GdpAndOutput, Frequency = "Quarterly",
            Units = "Billions of Dollars", SeasonalAdjustment = "Seasonally Adjusted Annual Rate",
        };

        DbContext.Set<FredSeries>().AddRange(_fedFundsSeries, _cpiSeries, _gdpSeries);
        DbContext.Set<FredObservation>().AddRange(
            new FredObservation { FredSeriesId = _fedFundsSeries.Id, Date = new DateOnly(2025, 1, 1), Value = 4.33m },
            new FredObservation { FredSeriesId = _fedFundsSeries.Id, Date = new DateOnly(2025, 2, 1), Value = 4.33m },
            new FredObservation { FredSeriesId = _fedFundsSeries.Id, Date = new DateOnly(2025, 3, 1), Value = 4.50m },
            new FredObservation { FredSeriesId = _fedFundsSeries.Id, Date = new DateOnly(2025, 6, 1), Value = 4.25m },
            new FredObservation { FredSeriesId = _fedFundsSeries.Id, Date = new DateOnly(2025, 9, 1), Value = 4.00m },

            new FredObservation { FredSeriesId = _cpiSeries.Id, Date = new DateOnly(2025, 1, 1), Value = 315.5m },
            new FredObservation { FredSeriesId = _cpiSeries.Id, Date = new DateOnly(2025, 2, 1), Value = 316.2m },
            new FredObservation { FredSeriesId = _cpiSeries.Id, Date = new DateOnly(2025, 3, 1), Value = 317.0m },

            new FredObservation { FredSeriesId = _gdpSeries.Id, Date = new DateOnly(2025, 1, 1), Value = 28500.0m },
            new FredObservation { FredSeriesId = _gdpSeries.Id, Date = new DateOnly(2025, 4, 1), Value = 28800.0m });

        await DbContext.SaveChangesAsync();
    }

    // ── GetEconomicIndicator ───────────────────────────────────────────

    [Fact]
    public async Task GetEconomicIndicator_SeriesFoundWithObservations_ReturnsFormattedTable() {
        var result = await Sut().GetEconomicIndicator("FEDFUNDS", "2025-01-01", "2025-12-31");

        result.Should().Contain("Federal Funds Effective Rate (FEDFUNDS)");
        result.Should().Contain("Units: Percent");
        result.Should().Contain("Frequency: Monthly");
        result.Should().Contain("| Date | Value |");
        result.Should().Contain("2025-01-01");
        result.Should().Contain(4.33m.ToString("G"));
        result.Should().Contain(4.50m.ToString("G"));
    }

    [Fact]
    public async Task GetEconomicIndicator_SeriesNotFound_ReturnsNotFoundMessage() {
        var result = await Sut().GetEconomicIndicator("NONEXISTENT");

        result.Should().Contain("not found");
        result.Should().Contain("NONEXISTENT");
        result.Should().Contain("SearchEconomicIndicators");
    }

    [Fact]
    public async Task GetEconomicIndicator_DateFiltering_ReturnsOnlyObservationsInRange() {
        var result = await Sut().GetEconomicIndicator("FEDFUNDS", "2025-02-01", "2025-03-31");

        result.Should().Contain("2025-02-01");
        result.Should().Contain("2025-03-01");
        result.Should().NotContain("2025-01-01");
        result.Should().NotContain("2025-06-01");
        result.Should().NotContain("2025-09-01");
    }

    [Fact]
    public async Task GetEconomicIndicator_MaxResultsLimit_ReturnsOnlyRequestedCount() {
        var result = await Sut().GetEconomicIndicator("FEDFUNDS", "2025-01-01", "2025-12-31", maxResults: 2);

        result.Should().Contain("2025-06-01");
        result.Should().Contain("2025-09-01");
        result.Should().NotContain("2025-01-01");
        result.Should().NotContain("2025-02-01");
        result.Should().NotContain("2025-03-01");
    }

    [Fact]
    public async Task GetEconomicIndicator_CaseInsensitiveSeriesId_FindsSeries() {
        var result = await Sut().GetEconomicIndicator("fedfunds", "2025-01-01", "2025-12-31");

        result.Should().Contain("Federal Funds Effective Rate (FEDFUNDS)");
    }

    [Fact]
    public async Task GetEconomicIndicator_NoObservationsInRange_ReturnsNoObservationsMessage() {
        var result = await Sut().GetEconomicIndicator("FEDFUNDS", "2020-01-01", "2020-12-31");

        result.Should().Contain("No observations found");
        result.Should().Contain("FEDFUNDS");
    }

    [Fact]
    public async Task GetEconomicIndicator_DefaultDatesUsedWhenOmitted_ReturnsRecentData() {
        DbContext.Set<FredObservation>().Add(new FredObservation {
            FredSeriesId = _fedFundsSeries.Id,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-3)),
            Value = 5.00m,
        });
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetEconomicIndicator("FEDFUNDS");

        result.Should().Contain("Federal Funds Effective Rate (FEDFUNDS)");
        result.Should().Contain("5");
    }

    [Fact]
    public async Task GetEconomicIndicator_ObservationsOrderedByDateAscending() {
        var result = await Sut().GetEconomicIndicator("FEDFUNDS", "2025-01-01", "2025-12-31");

        var jan = result.IndexOf("2025-01-01", StringComparison.Ordinal);
        var feb = result.IndexOf("2025-02-01", StringComparison.Ordinal);
        var mar = result.IndexOf("2025-03-01", StringComparison.Ordinal);
        var jun = result.IndexOf("2025-06-01", StringComparison.Ordinal);
        var sep = result.IndexOf("2025-09-01", StringComparison.Ordinal);

        jan.Should().BeLessThan(feb);
        feb.Should().BeLessThan(mar);
        mar.Should().BeLessThan(jun);
        jun.Should().BeLessThan(sep);
    }

    // ── GetLatestEconomicData ──────────────────────────────────────────

    [Fact]
    public async Task GetLatestEconomicData_AllCategories_ReturnsAllSeries() {
        var result = await Sut().GetLatestEconomicData();

        result.Should().Contain("Latest Economic Indicators");
        result.Should().Contain("FEDFUNDS");
        result.Should().Contain("CPIAUCSL");
        result.Should().Contain("GDP");
        result.Should().Contain("Federal Funds Effective Rate");
        result.Should().Contain("Consumer Price Index");
        result.Should().Contain("Gross Domestic Product");
    }

    [Fact]
    public async Task GetLatestEconomicData_SpecificCategoryFilter_ReturnsOnlyMatchingCategory() {
        var result = await Sut().GetLatestEconomicData("InterestRates");

        result.Should().Contain("FEDFUNDS");
        result.Should().Contain("Federal Funds Effective Rate");
        result.Should().NotContain("CPIAUCSL");
        result.Should().NotContain("GDP");
    }

    [Fact]
    public async Task GetLatestEconomicData_CaseInsensitiveCategoryFilter() {
        var result = await Sut().GetLatestEconomicData("interestrates");

        result.Should().Contain("FEDFUNDS");
        result.Should().NotContain("CPIAUCSL");
    }

    [Fact]
    public async Task GetLatestEconomicData_EmptyDatabase_ReturnsNoSeriesMessage() {
        // Wipe the seeded data from THIS test's instance — Respawn runs before each test, but
        // InitializeAsync re-seeds. To test the empty path we delete in-place.
        DbContext.Set<FredObservation>().RemoveRange(DbContext.Set<FredObservation>().ToList());
        DbContext.Set<FredSeries>().RemoveRange(DbContext.Set<FredSeries>().ToList());
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetLatestEconomicData();

        result.Should().Contain("No economic indicator series found");
    }

    [Fact]
    public async Task GetLatestEconomicData_ShowsLatestObservationValues() {
        var result = await Sut().GetLatestEconomicData();

        result.Should().Contain("2025-09-01");
        result.Should().Contain("4");

        result.Should().Contain("2025-03-01");
        result.Should().Contain("317");

        result.Should().Contain("2025-04-01");
        result.Should().Contain("28800");
    }

    [Fact]
    public async Task GetLatestEconomicData_InvalidCategory_ReturnsAllSeries() {
        var result = await Sut().GetLatestEconomicData("InvalidCategory");

        result.Should().Contain("FEDFUNDS");
        result.Should().Contain("CPIAUCSL");
        result.Should().Contain("GDP");
    }

    [Fact]
    public async Task GetLatestEconomicData_RendersMarkdownTableHeaders() {
        var result = await Sut().GetLatestEconomicData();

        result.Should().Contain("| Series | Title | Latest Date | Value | Units |");
        result.Should().Contain("|--------|-------|-------------|-------|-------|");
    }

    [Fact]
    public async Task GetLatestEconomicData_GroupsByCategory() {
        var result = await Sut().GetLatestEconomicData();

        result.Should().Contain($"**{FredSeriesCategory.InterestRates}**");
        result.Should().Contain($"**{FredSeriesCategory.Inflation}**");
        result.Should().Contain($"**{FredSeriesCategory.GdpAndOutput}**");
    }

    // ── SearchEconomicIndicators ───────────────────────────────────────

    [Fact]
    public async Task SearchEconomicIndicators_MatchesBySeriesId_ReturnsResults() {
        var result = await Sut().SearchEconomicIndicators("FEDFUNDS");

        result.Should().Contain("FEDFUNDS");
        result.Should().Contain("Federal Funds Effective Rate");
        result.Should().Contain("InterestRates");
    }

    [Fact]
    public async Task SearchEconomicIndicators_MatchesByTitleKeyword_ReturnsResults() {
        var result = await Sut().SearchEconomicIndicators("Consumer Price");

        result.Should().Contain("CPIAUCSL");
        result.Should().Contain("Consumer Price Index");
    }

    [Fact]
    public async Task SearchEconomicIndicators_CaseInsensitiveSearch() {
        var result = await Sut().SearchEconomicIndicators("gross domestic");

        result.Should().Contain("GDP");
        result.Should().Contain("Gross Domestic Product");
    }

    [Fact]
    public async Task SearchEconomicIndicators_NoMatches_ReturnsNotFoundMessage() {
        var result = await Sut().SearchEconomicIndicators("zzzznonexistent");

        result.Should().Contain("No series found matching");
        result.Should().Contain("zzzznonexistent");
    }

    [Fact]
    public async Task SearchEconomicIndicators_ResultLimit_RespectsMaxResults() {
        var result = await Sut().SearchEconomicIndicators("e", maxResults: 1);

        var dataLines = result.Split('\n')
            .Where(l => l.StartsWith("| ") && !l.Contains("Series ID") && !l.Contains("---"))
            .ToList();

        dataLines.Should().HaveCount(1);
    }

    [Fact]
    public async Task SearchEconomicIndicators_PartialSeriesIdMatch_ReturnsResults() {
        var result = await Sut().SearchEconomicIndicators("FED");

        result.Should().Contain("FEDFUNDS");
    }

    [Fact]
    public async Task SearchEconomicIndicators_RendersMarkdownTableHeaders() {
        var result = await Sut().SearchEconomicIndicators("GDP");

        result.Should().Contain("| Series ID | Title | Category | Frequency | Units |");
        result.Should().Contain("|-----------|-------|----------|-----------|-------|");
    }

    [Fact]
    public async Task SearchEconomicIndicators_MultipleMatches_ReturnsAll() {
        var result = await Sut().SearchEconomicIndicators("a");

        result.Should().Contain("FEDFUNDS");
        result.Should().Contain("CPIAUCSL");
    }
}
