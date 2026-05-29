using Equibles.CommonStocks.Data.Models;
using Equibles.Finra.Data.Models;
using Equibles.Finra.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Finra;

// The existing GetAllSettlementDates test runs against the in-memory provider, which only
// exercises the non-relational DISTINCT fallback. On a relational provider the method returns
// a raw recursive-CTE query (SqlQueryRaw). ShortActivityController composes
// `.OrderByDescending(d => d)` onto that IQueryable before materializing — EF must wrap the
// `WITH RECURSIVE ...` SQL as a derived table for Postgres. This pins that the real production
// path yields the distinct settlement dates newest-first under that composition.
[Collection(ParadeDbCollection.Name)]
public class ShortInterestRepositoryGetAllSettlementDatesRelationalTests : ParadeDbMcpTestBase
{
    public ShortInterestRepositoryGetAllSettlementDatesRelationalTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetAllSettlementDates_OnRelationalProvider_ComposesAndReturnsDistinctNewestFirst()
    {
        var june = new DateOnly(2024, 6, 15);
        var may = new DateOnly(2024, 5, 31);
        var april = new DateOnly(2024, 4, 30);

        DbContext.Add(Row("SI-MAY", may));
        DbContext.Add(Row("SI-JUN1", june));
        DbContext.Add(Row("SI-JUN2", june)); // same date, different stock — must collapse to one
        DbContext.Add(Row("SI-APR", april));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = new ShortInterestRepository(verify);

        // Mirror ShortActivityController: compose OrderByDescending over the raw recursive-CTE
        // query, then materialize.
        var dates = await sut.GetAllSettlementDates().OrderByDescending(d => d).ToListAsync();

        dates.Should().Equal(june, may, april);
    }

    // Each row gets its own CommonStock parent so the FK_ShortInterest_CommonStock constraint
    // (enforced on the real provider, ignored in-memory) is satisfied.
    private static ShortInterest Row(string ticker, DateOnly settlementDate) =>
        new()
        {
            CommonStock = new CommonStock { Ticker = ticker, Name = ticker },
            SettlementDate = settlementDate,
            CurrentShortPosition = 1000,
        };
}
