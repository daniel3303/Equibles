using Equibles.Fred.Data.Models;
using Equibles.Fred.Mcp.Tools;
using Equibles.Fred.Repositories;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// GetEconomicCalendar renders the stored FRED release calendar. Pin the core
/// contract: dates inside the requested range render chronologically with the
/// release name and the series the release updates, dates outside the range
/// are excluded, and an empty range returns the no-data message instead of an
/// empty table.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FredToolsGetEconomicCalendarTests : ParadeDbMcpTestBase
{
    private FredRelease _cpiRelease;
    private FredRelease _employmentRelease;

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

        _cpiRelease = new FredRelease { ReleaseId = 10, Name = "Consumer Price Index" };
        _employmentRelease = new FredRelease { ReleaseId = 50, Name = "Employment Situation" };
        DbContext.Set<FredRelease>().AddRange(_cpiRelease, _employmentRelease);

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
                // Outside the queried range — must not render.
                new FredReleaseDate { FredReleaseId = _cpiRelease.Id, Date = new(2026, 8, 12) }
            );

        await DbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task GetEconomicCalendar_RangeWithReleases_RendersChronologicalRowsWithSeries()
    {
        var result = await Sut().GetEconomicCalendar("2026-06-01", "2026-06-30");

        result.Should().Contain("| 2026-06-05 | Employment Situation | UNRATE |");
        result.Should().Contain("| 2026-06-11 | Consumer Price Index | CPIAUCSL, CPILFESL |");
        result.Should().NotContain("2026-08-12");

        // Chronological: the employment print comes before the CPI print.
        result
            .IndexOf("Employment Situation", StringComparison.Ordinal)
            .Should()
            .BeLessThan(result.IndexOf("Consumer Price Index", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetEconomicCalendar_EmptyRange_ReturnsNoDataMessage()
    {
        var result = await Sut().GetEconomicCalendar("2026-09-01", "2026-09-30");

        result.Should().Contain("No economic releases between 2026-09-01 and 2026-09-30.");
    }
}
