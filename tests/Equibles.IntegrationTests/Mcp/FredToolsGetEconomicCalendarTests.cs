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

        result.Should().Contain("Invalid minImportance 'critical'");
        result.Should().NotContain("Consumer Price Index");
    }

    [Fact]
    public async Task GetEconomicCalendar_EmptyRange_ReturnsNoDataMessage()
    {
        var result = await Sut().GetEconomicCalendar("2026-09-01", "2026-09-30");

        result.Should().Contain("No economic releases between 2026-09-01 and 2026-09-30.");
    }
}
