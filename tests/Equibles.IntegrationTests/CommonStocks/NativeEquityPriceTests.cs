using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.CommonStocks;

[Collection(ParadeDbCollection.Name)]
public class NativeEquityPriceTests : ParadeDbMcpTestBase
{
    public NativeEquityPriceTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task NativeListingPrices_ReadWithoutLegacyStorage_AndKeepVenueSeriesSeparate()
    {
        var issuer = new EquityIssuer { Name = "Shared issuer" };
        var security = new EquitySecurity { Issuer = issuer };
        var lisbon = new EquityListing
        {
            Security = security,
            Ticker = "SAME",
            MarketIdentifierCode = "XLIS",
        };
        var london = new EquityListing
        {
            Security = security,
            Ticker = "SAME",
            MarketIdentifierCode = "XLON",
        };
        DbContext.AddRange(
            new EquityDailyStockPrice
            {
                Listing = lisbon,
                Date = new DateOnly(2026, 9, 10),
                Close = 12.3456m,
                Volume = 123456,
            },
            new EquityDailyStockPrice
            {
                Listing = london,
                Date = new DateOnly(2026, 9, 10),
                Close = 987.6543m,
                Volume = 654321,
            }
        );
        await DbContext.SaveChangesAsync();
        (await DbContext.Set<DailyStockPrice>().CountAsync()).Should().Be(0);
        (await DbContext.Set<LegacyDailyStockPrice>().CountAsync()).Should().Be(0);
        (await DbContext.Set<LegacyEquityListing>().CountAsync()).Should().Be(0);
        var prices = new DailyStockPriceRepository(DbContext);
        (await prices.GetByListing(lisbon.Id).SingleAsync()).Close.Should().Be(12.3456m);
        (await prices.GetByListing(london.Id).SingleAsync()).Close.Should().Be(987.6543m);
        (await prices.GetByListing(lisbon.Id).SingleAsync()).CommonStockId.Should().Be(issuer.Id);
    }

    [Fact]
    public async Task TransitionalWriters_PreserveBothObservationsWithTheSameId_AndResettleAtomically()
    {
        var stock = new CommonStock { Ticker = "PRIMARY", SecondaryTickers = ["CLASS-B"] };
        var rowId = Guid.NewGuid();
        var created = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var exact = new DailyStockPrice
        {
            Id = rowId,
            CommonStock = stock,
            ListedTicker = "CLASS-B",
            Date = new DateOnly(2020, 1, 2),
            Open = 1.2345m,
            High = 3.4567m,
            Low = 0.9876m,
            Close = 2.3456m,
            AdjustedClose = 1.8765m,
            Volume = 1234567890,
            CreationTime = created,
        };
        var unknown = new LegacyDailyStockPrice
        {
            Id = rowId,
            CommonStock = stock,
            Date = exact.Date,
            Open = 11.2345m,
            High = 13.4567m,
            Low = 10.9876m,
            Close = 12.3456m,
            AdjustedClose = 11.8765m,
            Volume = 9876543210,
            CreationTime = created.AddSeconds(1),
        };
        DbContext.AddRange(exact, unknown);
        await DbContext.SaveChangesAsync();
        var listing = await new EquityListingRepository(DbContext)
            .GetByLegacyKey(stock.Id, "CLASS-B")
            .SingleAsync();
        var native = await new EquityDailyStockPriceRepository(DbContext)
            .GetByListing(listing.Id)
            .SingleAsync();
        native
            .Should()
            .BeEquivalentTo(
                exact,
                options =>
                    options
                        .Excluding(row => row.CommonStock)
                        .Excluding(row => row.CommonStockId)
                        .Excluding(row => row.ListedTicker)
            );
        native.SourceTicker.Should().Be("CLASS-B");
        var unresolved = await new UnattributedDailyStockPriceRepository(DbContext)
            .GetByIssuer(stock.Id)
            .SingleAsync();
        unresolved
            .Should()
            .BeEquivalentTo(
                unknown,
                options =>
                    options.Excluding(row => row.CommonStock).Excluding(row => row.CommonStockId)
            );
        await using (var transaction = await DbContext.Database.BeginTransactionAsync())
        {
            exact.Close = 2.9876m;
            await DbContext.SaveChangesAsync();
            await DbContext.Entry(native).ReloadAsync();
            native.Close.Should().Be(2.9876m);
            await transaction.RollbackAsync();
        }
        DbContext.ChangeTracker.Clear();
        (await DbContext.Set<EquityDailyStockPrice>().SingleAsync()).Close.Should().Be(2.3456m);
        (await DbContext.Set<DailyStockPrice>().SingleAsync()).Close.Should().Be(2.3456m);
        await DbContext.Set<DailyStockPrice>().Where(row => row.Id == rowId).ExecuteDeleteAsync();
        (await DbContext.Set<EquityDailyStockPrice>().CountAsync()).Should().Be(0);
        (await DbContext.Set<UnattributedDailyStockPrice>().CountAsync()).Should().Be(1);
    }
}
