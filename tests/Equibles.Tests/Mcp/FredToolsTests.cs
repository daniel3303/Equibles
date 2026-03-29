using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data;
using Equibles.Errors.Repositories;
using Equibles.Fred.Data;
using Equibles.Fred.Data.Models;
using Equibles.Fred.Mcp.Tools;
using Equibles.Fred.Repositories;
using Equibles.Tests.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.Tests.Mcp;

public class FredToolsTests : IDisposable {
    private readonly EquiblesDbContext _dbContext;
    private readonly FredSeriesRepository _seriesRepository;
    private readonly FredObservationRepository _observationRepository;
    private readonly ErrorManager _errorManager;
    private readonly ILogger<FredTools> _logger;
    private readonly FredTools _sut;

    private readonly FredSeries _fedFundsSeries;
    private readonly FredSeries _cpiSeries;
    private readonly FredSeries _gdpSeries;

    public FredToolsTests() {
        _dbContext = TestDbContextFactory.Create(
            new FredModuleConfiguration(),
            new ErrorsModuleConfiguration()
        );

        _seriesRepository = new FredSeriesRepository(_dbContext);
        _observationRepository = new FredObservationRepository(_dbContext);

        var errorRepository = new ErrorRepository(_dbContext);
        _errorManager = new ErrorManager(errorRepository);

        _logger = Substitute.For<ILogger<FredTools>>();

        _fedFundsSeries = new FredSeries {
            SeriesId = "FEDFUNDS",
            Title = "Federal Funds Effective Rate",
            Category = FredSeriesCategory.InterestRates,
            Frequency = "Monthly",
            Units = "Percent",
            SeasonalAdjustment = "Not Seasonally Adjusted"
        };

        _cpiSeries = new FredSeries {
            SeriesId = "CPIAUCSL",
            Title = "Consumer Price Index for All Urban Consumers",
            Category = FredSeriesCategory.Inflation,
            Frequency = "Monthly",
            Units = "Index 1982-1984=100",
            SeasonalAdjustment = "Seasonally Adjusted"
        };

        _gdpSeries = new FredSeries {
            SeriesId = "GDP",
            Title = "Gross Domestic Product",
            Category = FredSeriesCategory.GdpAndOutput,
            Frequency = "Quarterly",
            Units = "Billions of Dollars",
            SeasonalAdjustment = "Seasonally Adjusted Annual Rate"
        };

        _dbContext.Set<FredSeries>().AddRange(_fedFundsSeries, _cpiSeries, _gdpSeries);

        _dbContext.Set<FredObservation>().AddRange(
            new FredObservation { FredSeriesId = _fedFundsSeries.Id, Date = new DateOnly(2025, 1, 1), Value = 4.33m },
            new FredObservation { FredSeriesId = _fedFundsSeries.Id, Date = new DateOnly(2025, 2, 1), Value = 4.33m },
            new FredObservation { FredSeriesId = _fedFundsSeries.Id, Date = new DateOnly(2025, 3, 1), Value = 4.50m },
            new FredObservation { FredSeriesId = _fedFundsSeries.Id, Date = new DateOnly(2025, 6, 1), Value = 4.25m },
            new FredObservation { FredSeriesId = _fedFundsSeries.Id, Date = new DateOnly(2025, 9, 1), Value = 4.00m },

            new FredObservation { FredSeriesId = _cpiSeries.Id, Date = new DateOnly(2025, 1, 1), Value = 315.5m },
            new FredObservation { FredSeriesId = _cpiSeries.Id, Date = new DateOnly(2025, 2, 1), Value = 316.2m },
            new FredObservation { FredSeriesId = _cpiSeries.Id, Date = new DateOnly(2025, 3, 1), Value = 317.0m },

            new FredObservation { FredSeriesId = _gdpSeries.Id, Date = new DateOnly(2025, 1, 1), Value = 28500.0m },
            new FredObservation { FredSeriesId = _gdpSeries.Id, Date = new DateOnly(2025, 4, 1), Value = 28800.0m }
        );

        _dbContext.SaveChanges();

        _sut = new FredTools(_seriesRepository, _observationRepository, _errorManager, _logger);
    }

    public void Dispose() {
        _dbContext.Dispose();
    }

    // ── GetEconomicIndicator ───────────────────────────────────────────

    [Fact]
    public async Task GetEconomicIndicator_SeriesFoundWithObservations_ReturnsFormattedTable() {
        var result = await _sut.GetEconomicIndicator("FEDFUNDS", "2025-01-01", "2025-12-31");

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
        var result = await _sut.GetEconomicIndicator("NONEXISTENT");

        result.Should().Contain("not found");
        result.Should().Contain("NONEXISTENT");
        result.Should().Contain("SearchEconomicIndicators");
    }

    [Fact]
    public async Task GetEconomicIndicator_DateFiltering_ReturnsOnlyObservationsInRange() {
        var result = await _sut.GetEconomicIndicator("FEDFUNDS", "2025-02-01", "2025-03-31");

        result.Should().Contain("2025-02-01");
        result.Should().Contain("2025-03-01");
        result.Should().NotContain("2025-01-01");
        result.Should().NotContain("2025-06-01");
        result.Should().NotContain("2025-09-01");
    }

    [Fact]
    public async Task GetEconomicIndicator_MaxResultsLimit_ReturnsOnlyRequestedCount() {
        var result = await _sut.GetEconomicIndicator("FEDFUNDS", "2025-01-01", "2025-12-31", maxResults: 2);

        // maxResults=2 takes the 2 newest observations (OrderByDescending then Take)
        // which are Sep and Jun. They are then re-ordered by date ascending for display.
        result.Should().Contain("2025-06-01");
        result.Should().Contain("2025-09-01");
        result.Should().NotContain("2025-01-01");
        result.Should().NotContain("2025-02-01");
        result.Should().NotContain("2025-03-01");
    }

    [Fact]
    public async Task GetEconomicIndicator_CaseInsensitiveSeriesId_FindsSeries() {
        var result = await _sut.GetEconomicIndicator("fedfunds", "2025-01-01", "2025-12-31");

        result.Should().Contain("Federal Funds Effective Rate (FEDFUNDS)");
    }

    [Fact]
    public async Task GetEconomicIndicator_NoObservationsInRange_ReturnsNoObservationsMessage() {
        var result = await _sut.GetEconomicIndicator("FEDFUNDS", "2020-01-01", "2020-12-31");

        result.Should().Contain("No observations found");
        result.Should().Contain("FEDFUNDS");
    }

    [Fact]
    public async Task GetEconomicIndicator_DefaultDatesUsedWhenOmitted_ReturnsRecentData() {
        // Add an observation within the default 1-year window
        _dbContext.Set<FredObservation>().Add(
            new FredObservation {
                FredSeriesId = _fedFundsSeries.Id,
                Date = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-3)),
                Value = 5.00m
            }
        );
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetEconomicIndicator("FEDFUNDS");

        result.Should().Contain("Federal Funds Effective Rate (FEDFUNDS)");
        result.Should().Contain("5");
    }

    [Fact]
    public async Task GetEconomicIndicator_ObservationsOrderedByDateAscending() {
        var result = await _sut.GetEconomicIndicator("FEDFUNDS", "2025-01-01", "2025-12-31");

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
        var result = await _sut.GetLatestEconomicData();

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
        var result = await _sut.GetLatestEconomicData("InterestRates");

        result.Should().Contain("FEDFUNDS");
        result.Should().Contain("Federal Funds Effective Rate");
        result.Should().NotContain("CPIAUCSL");
        result.Should().NotContain("GDP");
    }

    [Fact]
    public async Task GetLatestEconomicData_CaseInsensitiveCategoryFilter() {
        var result = await _sut.GetLatestEconomicData("interestrates");

        result.Should().Contain("FEDFUNDS");
        result.Should().NotContain("CPIAUCSL");
    }

    [Fact]
    public async Task GetLatestEconomicData_EmptyDatabase_ReturnsNoSeriesMessage() {
        var emptyDbContext = TestDbContextFactory.Create(
            new FredModuleConfiguration(),
            new ErrorsModuleConfiguration()
        );
        var emptySeriesRepo = new FredSeriesRepository(emptyDbContext);
        var emptyObsRepo = new FredObservationRepository(emptyDbContext);
        var emptyErrorRepo = new ErrorRepository(emptyDbContext);
        var emptyErrorManager = new ErrorManager(emptyErrorRepo);

        var tools = new FredTools(emptySeriesRepo, emptyObsRepo, emptyErrorManager, _logger);

        var result = await tools.GetLatestEconomicData();

        result.Should().Contain("No economic indicator series found");

        emptyDbContext.Dispose();
    }

    [Fact]
    public async Task GetLatestEconomicData_ShowsLatestObservationValues() {
        var result = await _sut.GetLatestEconomicData();

        // Latest FEDFUNDS observation is Sep 2025 at 4.00
        result.Should().Contain("2025-09-01");
        result.Should().Contain("4");

        // Latest CPI observation is Mar 2025 at 317.0
        result.Should().Contain("2025-03-01");
        result.Should().Contain("317");

        // Latest GDP observation is Apr 2025 at 28800.0
        result.Should().Contain("2025-04-01");
        result.Should().Contain("28800");
    }

    [Fact]
    public async Task GetLatestEconomicData_InvalidCategory_ReturnsAllSeries() {
        var result = await _sut.GetLatestEconomicData("InvalidCategory");

        result.Should().Contain("FEDFUNDS");
        result.Should().Contain("CPIAUCSL");
        result.Should().Contain("GDP");
    }

    [Fact]
    public async Task GetLatestEconomicData_RendersMarkdownTableHeaders() {
        var result = await _sut.GetLatestEconomicData();

        result.Should().Contain("| Series | Title | Latest Date | Value | Units |");
        result.Should().Contain("|--------|-------|-------------|-------|-------|");
    }

    [Fact]
    public async Task GetLatestEconomicData_GroupsByCategory() {
        var result = await _sut.GetLatestEconomicData();

        result.Should().Contain($"**{FredSeriesCategory.InterestRates}**");
        result.Should().Contain($"**{FredSeriesCategory.Inflation}**");
        result.Should().Contain($"**{FredSeriesCategory.GdpAndOutput}**");
    }

    // ── SearchEconomicIndicators ───────────────────────────────────────

    [Fact]
    public async Task SearchEconomicIndicators_MatchesBySeriesId_ReturnsResults() {
        var result = await _sut.SearchEconomicIndicators("FEDFUNDS");

        result.Should().Contain("FEDFUNDS");
        result.Should().Contain("Federal Funds Effective Rate");
        result.Should().Contain("InterestRates");
    }

    [Fact]
    public async Task SearchEconomicIndicators_MatchesByTitleKeyword_ReturnsResults() {
        var result = await _sut.SearchEconomicIndicators("Consumer Price");

        result.Should().Contain("CPIAUCSL");
        result.Should().Contain("Consumer Price Index");
    }

    [Fact]
    public async Task SearchEconomicIndicators_CaseInsensitiveSearch() {
        var result = await _sut.SearchEconomicIndicators("gross domestic");

        result.Should().Contain("GDP");
        result.Should().Contain("Gross Domestic Product");
    }

    [Fact]
    public async Task SearchEconomicIndicators_NoMatches_ReturnsNotFoundMessage() {
        var result = await _sut.SearchEconomicIndicators("zzzznonexistent");

        result.Should().Contain("No series found matching");
        result.Should().Contain("zzzznonexistent");
    }

    [Fact]
    public async Task SearchEconomicIndicators_ResultLimit_RespectsMaxResults() {
        var result = await _sut.SearchEconomicIndicators("e", maxResults: 1);

        // "e" matches multiple series; with maxResults=1 only one should appear
        var seriesCount = CountOccurrences(result, "| ");
        // Header row (2 lines: header + separator) + category rows, but data rows should be limited
        // Simplest check: at most 1 data row beyond the header
        var dataLines = result.Split('\n')
            .Where(l => l.StartsWith("| ") && !l.Contains("Series ID") && !l.Contains("---"))
            .ToList();

        dataLines.Should().HaveCount(1);
    }

    [Fact]
    public async Task SearchEconomicIndicators_PartialSeriesIdMatch_ReturnsResults() {
        var result = await _sut.SearchEconomicIndicators("FED");

        result.Should().Contain("FEDFUNDS");
    }

    [Fact]
    public async Task SearchEconomicIndicators_RendersMarkdownTableHeaders() {
        var result = await _sut.SearchEconomicIndicators("GDP");

        result.Should().Contain("| Series ID | Title | Category | Frequency | Units |");
        result.Should().Contain("|-----------|-------|----------|-----------|-------|");
    }

    [Fact]
    public async Task SearchEconomicIndicators_MultipleMatches_ReturnsAll() {
        // All three seeded series contain common letter patterns; search for something broad
        var result = await _sut.SearchEconomicIndicators("a");

        // "a" appears in FEDFUNDS (Federal), CPIAUCSL (CPIAUCSL/Consumer/Adjusted), GDP title doesn't,
        // but at least FEDFUNDS and CPIAUCSL should match
        result.Should().Contain("FEDFUNDS");
        result.Should().Contain("CPIAUCSL");
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static int CountOccurrences(string source, string substring) {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(substring, index, StringComparison.Ordinal)) != -1) {
            count++;
            index += substring.Length;
        }
        return count;
    }
}
