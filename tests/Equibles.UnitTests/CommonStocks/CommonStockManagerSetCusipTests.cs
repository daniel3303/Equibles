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

// Lane B (coverage): exercises SetCusip — zero-hit today. The contract
// (doc-comment) says: "When the value actually changes, publishes
// StockCusipChanged. A no-op change publishes nothing."
public class CommonStockManagerSetCusipTests
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
    public async Task SetCusip_NewValueDifferentFromCurrent_PublishesEventAndSaves()
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

        await sut.SetCusip(stock, "594918104");

        stock.Cusip.Should().Be("594918104");
        await publishEndpoint
            .Received(1)
            .Publish(
                Arg.Is<StockCusipChanged>(e =>
                    e.CommonStockId == stock.Id
                    && e.PreviousCusip == "037833100"
                    && e.Cusip == "594918104"
                )
            );
        await repo.Received(1).SaveChanges();
    }
}
