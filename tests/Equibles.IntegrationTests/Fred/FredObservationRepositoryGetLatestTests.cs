using Equibles.Fred.Data.Models;
using Equibles.Fred.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Fred;

/// <summary>
/// Pins <see cref="FredObservationRepository.GetLatestPerSeries"/> — the
/// dashboard-front-page query that powers the macro indicator tiles. Three
/// load-bearing pieces in one expression: (1) <c>Where(o =&gt; o.Value != null)</c>
/// must skip rows where FRED reports "."; (2) <c>GroupBy(FredSeriesId)</c> must
/// partition; (3) <c>OrderByDescending(Date).First()</c> picks the newest per
/// series. EF Core's GroupBy-with-non-aggregated-Select translates differently
/// per provider — this only round-trips correctly against the real Postgres
/// fixture.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FredObservationRepositoryGetLatestTests : ParadeDbMcpTestBase
{
    public FredObservationRepositoryGetLatestTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetLatestPerSeries_TwoSeriesWithNullAndMultipleDates_ReturnsNewestNonNullPerSeries()
    {
        var seriesA = new FredSeries { SeriesId = "DGS10", Title = "10-Year Treasury" };
        var seriesB = new FredSeries { SeriesId = "UNRATE", Title = "Unemployment Rate" };
        DbContext.Add(seriesA);
        DbContext.Add(seriesB);

        // seriesA: two observations, the older one with a real value, the newer
        // one with null (FRED reports "." for missing dates). The query must
        // SKIP the null row and surface the older real value as "latest".
        DbContext.Add(
            new FredObservation
            {
                FredSeriesId = seriesA.Id,
                Date = new DateOnly(2024, 12, 1),
                Value = 4.25m,
            }
        );
        DbContext.Add(
            new FredObservation
            {
                FredSeriesId = seriesA.Id,
                Date = new DateOnly(2024, 12, 8),
                Value = null,
            }
        );

        // seriesB: two real values — must pick the newer one.
        DbContext.Add(
            new FredObservation
            {
                FredSeriesId = seriesB.Id,
                Date = new DateOnly(2024, 11, 1),
                Value = 4.1m,
            }
        );
        DbContext.Add(
            new FredObservation
            {
                FredSeriesId = seriesB.Id,
                Date = new DateOnly(2024, 12, 1),
                Value = 4.2m,
            }
        );

        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = new FredObservationRepository(verify);

        var latest = (await sut.GetLatestPerSeries().AsNoTracking().ToListAsync()).ToDictionary(o =>
            o.FredSeriesId
        );

        latest.Should().HaveCount(2);
        latest[seriesA.Id].Date.Should().Be(new DateOnly(2024, 12, 1));
        latest[seriesA.Id].Value.Should().Be(4.25m);
        latest[seriesB.Id].Date.Should().Be(new DateOnly(2024, 12, 1));
        latest[seriesB.Id].Value.Should().Be(4.2m);
    }

    [Fact]
    public async Task GetPreviousPerSeries_SkipsNullsAndSingleObservationSeries_ReturnsSecondNewest()
    {
        var seriesA = new FredSeries { SeriesId = "DGS10", Title = "10-Year Treasury" };
        var seriesB = new FredSeries { SeriesId = "UNRATE", Title = "Unemployment Rate" };
        DbContext.Add(seriesA);
        DbContext.Add(seriesB);

        // seriesA: three rows — newest is null (FRED "."), so "previous" must be
        // the second-newest REAL value, counting only non-null rows.
        DbContext.Add(
            new FredObservation
            {
                FredSeriesId = seriesA.Id,
                Date = new DateOnly(2024, 11, 1),
                Value = 4.0m,
            }
        );
        DbContext.Add(
            new FredObservation
            {
                FredSeriesId = seriesA.Id,
                Date = new DateOnly(2024, 12, 1),
                Value = 4.25m,
            }
        );
        DbContext.Add(
            new FredObservation
            {
                FredSeriesId = seriesA.Id,
                Date = new DateOnly(2024, 12, 8),
                Value = null,
            }
        );

        // seriesB: a single observation — no previous row must be produced for it.
        DbContext.Add(
            new FredObservation
            {
                FredSeriesId = seriesB.Id,
                Date = new DateOnly(2024, 12, 1),
                Value = 4.2m,
            }
        );

        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = new FredObservationRepository(verify);

        var previous = await sut.GetPreviousPerSeries().AsNoTracking().ToListAsync();

        previous.Should().ContainSingle();
        previous[0].FredSeriesId.Should().Be(seriesA.Id);
        previous[0].Date.Should().Be(new DateOnly(2024, 11, 1));
        previous[0].Value.Should().Be(4.0m);
    }
}
