using Equibles.Fred.Data.Models;
using Equibles.Fred.Mcp.Tools;
using Equibles.Fred.Repositories;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// GetEconomicCalendar renders the stored FRED release calendar. Pin the core
/// contract: dates inside the requested range render chronologically with the
/// release name, its importance tier, and the series the release updates; dates
/// outside the range are excluded; minImportance drops the lower tiers (the
/// daily rate prints that otherwise drown the tier-1 releases); and an empty
/// range returns the no-data message instead of an empty table.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FredToolsGetEconomicCalendarTests : ParadeDbMcpTestBase
{
    private FredRelease _cpiRelease;
    private FredRelease _employmentRelease;
    private FredRelease _sofrRelease;

    public FredToolsGetEconomicCalendarTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private FredTools Sut() =>
        new(
            new FredSeriesRepository(DbContext),
            new FredObservationRepository(DbContext),
            new FredReleaseDateRepository(DbContext),
            ErrorManager,
            NullLogger<FredTools>()
        );

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        _cpiRelease = new FredRelease
        {
            ReleaseId = 10,
            Name = "Consumer Price Index",
            Importance = FredReleaseImportance.High,
        };
        _employmentRelease = new FredRelease
        {
            ReleaseId = 50,
            Name = "Employment Situation",
            Importance = FredReleaseImportance.High,
        };
        _sofrRelease = new FredRelease
        {
            ReleaseId = 445,
            Name = "Secured Overnight Financing Rate Data",
            Importance = FredReleaseImportance.Low,
        };
        DbContext.Set<FredRelease>().AddRange(_cpiRelease, _employmentRelease, _sofrRelease);

        DbContext
            .Set<FredSeries>()
            .AddRange(
                new FredSeries
                {
                    SeriesId = "CPIAUCSL",
                    Title = "Consumer Price Index for All Urban Consumers",
                    Category = FredSeriesCategory.Inflation,
                    FredReleaseId = _cpiRelease.Id,
                },
                new FredSeries
                {
                    SeriesId = "CPILFESL",
                    Title = "CPI Less Food and Energy",
                    Category = FredSeriesCategory.Inflation,
                    FredReleaseId = _cpiRelease.Id,
                },
                new FredSeries
                {
                    SeriesId = "UNRATE",
                    Title = "Unemployment Rate",
                    Category = FredSeriesCategory.Employment,
                    FredReleaseId = _employmentRelease.Id,
                },
                new FredSeries
                {
                    SeriesId = "SOFR",
                    Title = "Secured Overnight Financing Rate",
                    Category = FredSeriesCategory.InterestRates,
                    FredReleaseId = _sofrRelease.Id,
                }
            );

        DbContext
            .Set<FredReleaseDate>()
            .AddRange(
                new FredReleaseDate
                {
                    FredReleaseId = _employmentRelease.Id,
                    Date = new(2026, 6, 5),
                },
                new FredReleaseDate { FredReleaseId = _cpiRelease.Id, Date = new(2026, 6, 11) },
                // A daily low-tier print inside the range — filtered out by minImportance.
                new FredReleaseDate { FredReleaseId = _sofrRelease.Id, Date = new(2026, 6, 11) },
                // Outside the queried range — must not render.
                new FredReleaseDate { FredReleaseId = _cpiRelease.Id, Date = new(2026, 8, 12) }
            );

        await DbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task GetEconomicCalendar_RangeWithReleases_RendersChronologicalRowsWithTierAndSeries()
    {
        var result = await Sut().GetEconomicCalendar("2026-06-01", "2026-06-30");

        result.Should().Contain("| 2026-06-05 | Employment Situation | High | UNRATE |");
        result
            .Should()
            .Contain("| 2026-06-11 | Consumer Price Index | High | CPIAUCSL, CPILFESL |");
        result
            .Should()
            .Contain("| 2026-06-11 | Secured Overnight Financing Rate Data | Low | SOFR |");
        result.Should().NotContain("2026-08-12");

        // Chronological: the employment print comes before the CPI print.
        result
            .IndexOf("Employment Situation", StringComparison.Ordinal)
            .Should()
            .BeLessThan(result.IndexOf("Consumer Price Index", StringComparison.Ordinal));

        // Within a day, higher tiers render first: CPI (High) before SOFR (Low).
        result
            .IndexOf("Consumer Price Index", StringComparison.Ordinal)
            .Should()
            .BeLessThan(
                result.IndexOf("Secured Overnight Financing Rate Data", StringComparison.Ordinal)
            );
    }

    [Fact]
    public async Task GetEconomicCalendar_MinImportanceHigh_DropsLowerTiers()
    {
        var result = await Sut()
            .GetEconomicCalendar("2026-06-01", "2026-06-30", minImportance: "high");

        result.Should().Contain("Employment Situation");
        result.Should().Contain("Consumer Price Index");
        result.Should().NotContain("Secured Overnight Financing Rate Data");
    }

    [Fact]
    public async Task GetEconomicCalendar_InvalidMinImportance_ReturnsGuidance()
    {
        var result = await Sut()
            .GetEconomicCalendar("2026-06-01", "2026-06-30", minImportance: "critical");

        result.Should().Contain("Unknown minImportance 'critical'");
        result.Should().Contain("low, medium, high");
        result.Should().NotContain("Consumer Price Index");
    }

    [Fact]
    public async Task GetEconomicCalendar_NumericMinImportance_IsRejectedNotParsedByOrdinal()
    {
        // Enum.TryParse accepts "5", yielding an undefined tier that filters out
        // everything and would read as a factual "no releases" answer.
        var result = await Sut()
            .GetEconomicCalendar("2026-06-01", "2026-06-30", minImportance: "5");

        result.Should().Contain("Unknown minImportance '5'");
        result.Should().NotContain("No economic releases");
    }

    [Fact]
    public async Task GetEconomicCalendar_MalformedStartDate_ReturnsExplicitError()
    {
        // The old behavior silently swapped an unparseable date for the default
        // window and answered as if that was what the caller asked for.
        var result = await Sut().GetEconomicCalendar("next week");

        result.Should().Contain("Unknown startDate 'next week'");
        result.Should().Contain("yyyy-MM-dd");
    }

    [Fact]
    public async Task GetEconomicCalendar_MalformedEndDate_ReturnsExplicitError()
    {
        var result = await Sut().GetEconomicCalendar("2026-06-01", "06/30/2026");

        result.Should().Contain("Unknown endDate '06/30/2026'");
        result.Should().Contain("yyyy-MM-dd");
    }

    [Fact]
    public async Task GetEconomicCalendar_InvertedRange_NamesTheUserError()
    {
        var result = await Sut().GetEconomicCalendar("2026-08-01", "2026-07-01");

        result.Should().Contain("startDate (2026-08-01) is after endDate (2026-07-01)");
        result.Should().NotContain("No economic releases", "the error is the swap, not a data gap");
    }

    [Fact]
    public async Task GetEconomicCalendar_Truncated_HeaderNamesCoveredRangeAndFooterNotes()
    {
        // Three rows in June; a cap of 2 cuts the SOFR row. The header must not
        // claim the full requested range as covered, and the footer must say how
        // to see the rest — otherwise the tail silently reads as "nothing scheduled".
        var result = await Sut().GetEconomicCalendar("2026-06-01", "2026-06-30", maxResults: 2);

        result.Should().Contain("truncated at 2 rows, shown only through 2026-06-11");
        result.Should().Contain("Showing first 2 of 3 results");
        result.Should().NotContain("Secured Overnight Financing Rate Data");
    }

    [Fact]
    public async Task GetEconomicCalendar_NotTruncated_HeaderClaimsFullRangeWithoutNotes()
    {
        var result = await Sut().GetEconomicCalendar("2026-06-01", "2026-06-30");

        result.Should().Contain("Economic release calendar (2026-06-01 to 2026-06-30):");
        result.Should().NotContain("truncated");
        result.Should().NotContain("Showing first");
    }

    [Fact]
    public async Task GetEconomicCalendar_EmptyRange_ReturnsNoDataMessage()
    {
        var result = await Sut().GetEconomicCalendar("2026-09-01", "2026-09-30");

        result.Should().Contain("No economic releases between 2026-09-01 and 2026-09-30.");
    }
}
