using Equibles.CommonStocks.BusinessLogic;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Equibles.Messaging.Contracts.CommonStocks;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Equibles.UnitTests.CommonStocks;

// Lane A (adversarial): SetCusip's contract says "a no-op change publishes
// nothing." The equality check uses OrdinalIgnoreCase — a case-only difference
// must be treated as no-op. A regression switching to Ordinal would publish
// spurious StockCusipChanged events on every CompanySyncService run (SEC
// normalises CUSIPs to uppercase, but some historical data has lowercase).
public class CommonStockManagerSetCusipNoopTests
{
    private static EquiblesFinancialDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[] { new CommonStocksModuleConfiguration() }
        );
    }

    [Fact]
    public async Task SetCusip_SameValueDifferentCase_DoesNotPublishOrSave()
    {
        var db = NewDb();
        var repo = Substitute.For<CommonStockRepository>(db);
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var sut = new CommonStockManager(repo, publishEndpoint);
        var stock = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple",
            Cusip = "037833100",
        };

        await sut.SetCusip(stock, "037833100");

        stock.Cusip.Should().Be("037833100");
        await publishEndpoint.DidNotReceive().Publish(Arg.Any<StockCusipChanged>());
        await repo.DidNotReceive().SaveChanges();
    }
}
