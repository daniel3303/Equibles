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
        EquityIssuerRepository repo = Substitute.For<EquityIssuerRepository>(db);
        repo.GetAll().Returns(db.Set<EquityIssuer>());
        var publishEndpoint = Substitute.For<IBus>();
        EquityIdentityManager sut = new EquityIdentityManager(repo, publishEndpoint);
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "AAPL",
            Name: "Apple",
            Cusip: "037833100"
        );
        db.Set<EquityIssuer>().Add(stock);
        await db.SaveChangesAsync();

        await sut.SetCusip(stock, "594918104");

        stock.Presentation.Listing.Security.Cusip.Should().Be("594918104");
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
