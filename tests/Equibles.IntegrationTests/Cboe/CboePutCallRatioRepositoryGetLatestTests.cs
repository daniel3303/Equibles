using Equibles.Cboe.Data.Models;
using Equibles.Cboe.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Cboe;

/// <summary>
/// Pins <see cref="CboePutCallRatioRepository.GetLatestPerType"/> — the query behind the CBOE
/// put/call dashboard tile and the rating card's market backdrop. It returns the newest row per
/// ratio type via a correlated max-date filter (a plain GroupBy(...).Select(g =&gt; g.First()) is not
/// server-translatable and throws at runtime). Query translation is provider-specific, so these
/// only round-trip correctly against the real Postgres fixture.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class CboePutCallRatioRepositoryGetLatestTests : ParadeDbMcpTestBase
{
    public CboePutCallRatioRepositoryGetLatestTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetLatestPerType_TotalAndEquityWithMultipleDates_ReturnsNewestPerRatioType()
    {
        DbContext.Add(
            new CboePutCallRatio
            {
                RatioType = CboePutCallRatioType.Total,
                Date = new DateOnly(2024, 12, 23),
                PutCallRatio = 0.85m,
            }
        );
        DbContext.Add(
            new CboePutCallRatio
            {
                RatioType = CboePutCallRatioType.Total,
                Date = new DateOnly(2024, 12, 24),
                PutCallRatio = 0.92m,
            }
        );
        DbContext.Add(
            new CboePutCallRatio
            {
                RatioType = CboePutCallRatioType.Equity,
                Date = new DateOnly(2024, 12, 20),
                PutCallRatio = 0.65m,
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = new CboePutCallRatioRepository(verify);

        var latest = (await sut.GetLatestPerType().AsNoTracking().ToListAsync()).ToDictionary(r =>
            r.RatioType
        );

        latest.Should().HaveCount(2);
        latest[CboePutCallRatioType.Total].Date.Should().Be(new DateOnly(2024, 12, 24));
        latest[CboePutCallRatioType.Total].PutCallRatio.Should().Be(0.92m);
        latest[CboePutCallRatioType.Equity].Date.Should().Be(new DateOnly(2024, 12, 20));
    }

    [Fact]
    public async Task GetLatestPerType_FilteredToOneTypeThenFirst_TranslatesAndReturnsNewest()
    {
        // Regression: callers compose a further Where(type).FirstOrDefault() on top of
        // GetLatestPerType() (e.g. to read just the Equity ratio). The old
        // GroupBy(...).Select(g => g.First()) shape threw EF Core's KeyNotFoundException
        // "EmptyProjectionMember" at runtime when composed this way, 500-ing the caller. The
        // query must stay server-translatable and return the newest row for the filtered type.
        DbContext.Add(
            new CboePutCallRatio
            {
                RatioType = CboePutCallRatioType.Equity,
                Date = new DateOnly(2024, 12, 19),
                PutCallRatio = 0.70m,
            }
        );
        DbContext.Add(
            new CboePutCallRatio
            {
                RatioType = CboePutCallRatioType.Equity,
                Date = new DateOnly(2024, 12, 20),
                PutCallRatio = 0.65m,
            }
        );
        DbContext.Add(
            new CboePutCallRatio
            {
                RatioType = CboePutCallRatioType.Total,
                Date = new DateOnly(2024, 12, 24),
                PutCallRatio = 0.92m,
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = new CboePutCallRatioRepository(verify);

        var equity = await sut.GetLatestPerType()
            .Where(r => r.RatioType == CboePutCallRatioType.Equity)
            .AsNoTracking()
            .FirstOrDefaultAsync();

        equity.Should().NotBeNull();
        equity.Date.Should().Be(new DateOnly(2024, 12, 20));
        equity.PutCallRatio.Should().Be(0.65m);
    }
}
