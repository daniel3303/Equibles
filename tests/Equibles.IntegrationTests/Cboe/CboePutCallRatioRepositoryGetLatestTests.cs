using Equibles.Cboe.Data.Models;
using Equibles.Cboe.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Cboe;

/// <summary>
/// Pins <see cref="CboePutCallRatioRepository.GetLatestPerType"/> — the
/// query behind the CBOE put/call dashboard tile. Uses
/// <c>GroupBy(RatioType).Select(g =&gt; g.OrderByDescending(Date).First())</c>;
/// EF Core's GroupBy-with-non-aggregated-Select translates differently per
/// provider, so this only round-trips correctly against the real Postgres
/// fixture.
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
}
