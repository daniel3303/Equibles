using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.BusinessLogic;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.CorporateActions;

// The InMemory twins cannot see the (listing, date) unique index or the revision trigger; this
// runs the same-event collapse against both.
[Collection(ParadeDbCollection.Name)]
public class StockSplitCaptureSameEventPostgresTests(ParadeDbFixture fixture)
    : ParadeDbMcpTestBase(fixture)
{
    private static readonly DateOnly Day = new(2026, 1, 1);

    private StockSplitCaptureManager Manager() =>
        new(new StockSplitRepository(DbContext), new EquityIssuerRepository(DbContext));

    private static CapturedSplit At(
        DateOnly date,
        decimal denominator = 50m,
        StockSplitSource source = StockSplitSource.Yahoo
    ) =>
        new()
        {
            EffectiveDate = date,
            Numerator = 1m,
            Denominator = denominator,
            Source = source,
        };

    [Fact]
    public async Task Capture_OneEventServedTwice_IsOneRowAndReservingWritesNothing()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "SAME");
        DbContext.Add(issuer);
        await DbContext.SaveChangesAsync();
        var payload = new[] { At(Day), At(Day.AddDays(1)) };

        (await Manager().Capture(issuer.Id, "SAME", payload)).Should().Be(2);
        DbContext.ChangeTracker.Clear();
        var stored = await DbContext.Set<StockSplit>().SingleAsync();
        stored.EffectiveDate.Should().Be(Day.AddDays(1));
        stored.PriceAdjustmentAppliedTime = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        (await Manager().Capture(issuer.Id, "SAME", payload)).Should().Be(0);
        (await DbContext.Set<StockSplit>().AsNoTracking().SingleAsync())
            .PriceAdjustmentAppliedTime.Should()
            .NotBeNull("an unchanged definition keeps its marker");
    }

    [Fact]
    public async Task Capture_HigherSourceMovesTheDateUnderTheUniqueIndexAndReopensReconciliation()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "MOVE");
        DbContext.Add(issuer);
        await DbContext.SaveChangesAsync();
        await Manager().Capture(issuer.Id, "MOVE", [At(Day), At(Day.AddDays(3), denominator: 10m)]);
        DbContext.ChangeTracker.Clear();

        // The slot at Day+3 belongs to another ratio: the survivor stays, the exact-date row is
        // replaced by precedence, and nothing violates the index.
        await Manager()
            .Capture(issuer.Id, "MOVE", [At(Day.AddDays(3), source: StockSplitSource.External)]);
        DbContext.ChangeTracker.Clear();
        var rows = await DbContext
            .Set<StockSplit>()
            .AsNoTracking()
            .OrderBy(row => row.EffectiveDate)
            .ToListAsync();
        rows.Select(row => row.EffectiveDate).Should().Equal(Day, Day.AddDays(3));
        rows[1].Denominator.Should().Be(50m);

        // Now both rows are one event: the next observation collapses them onto the External date.
        (
            await Manager()
                .Capture(issuer.Id, "MOVE", [At(Day.AddDays(3), source: StockSplitSource.External)])
        )
            .Should()
            .Be(1);
        DbContext.ChangeTracker.Clear();
        var survivor = await DbContext.Set<StockSplit>().AsNoTracking().SingleAsync();
        survivor.EffectiveDate.Should().Be(Day.AddDays(3));
        survivor.Source.Should().Be(StockSplitSource.External);
    }
}
