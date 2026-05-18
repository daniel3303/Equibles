using Equibles.CommonStocks.BusinessLogic;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Messaging.Contracts.CommonStocks;
using MassTransit;
using NSubstitute;

namespace Equibles.IntegrationTests.CommonStocks;

public class CommonStockManagerSetCusipTests
{
    private readonly CommonStockManager _sut;
    private readonly IPublishEndpoint _publishEndpoint = Substitute.For<IPublishEndpoint>();
    private readonly CommonStockRepository _repository;

    public CommonStockManagerSetCusipTests()
    {
        var context = TestDbContextFactory.Create(new CommonStocksModuleConfiguration());
        _repository = new CommonStockRepository(context);
        _sut = new CommonStockManager(_repository, _publishEndpoint);
    }

    private async Task<CommonStock> SeedStock(string cusip)
    {
        var stock = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc",
            Cik = "0000320193",
            Cusip = cusip,
        };
        _repository.Add(stock);
        await _repository.SaveChanges();
        return stock;
    }

    // Contract: a real CUSIP change publishes StockCusipChanged (so Holdings
    // can backfill 13F data sets processed while the stock was unresolvable)
    // and persists the new value.
    [Fact]
    public async Task SetCusip_CusipChanged_PublishesEventAndPersists()
    {
        var stock = await SeedStock(null);

        await _sut.SetCusip(stock, "037833100");

        stock.Cusip.Should().Be("037833100");
        await _publishEndpoint
            .Received(1)
            .Publish(
                Arg.Is<StockCusipChanged>(e =>
                    e.CommonStockId == stock.Id
                    && e.Ticker == "AAPL"
                    && e.PreviousCusip == null
                    && e.Cusip == "037833100"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    // No-op change must not publish (avoids spurious backfills / event noise).
    [Fact]
    public async Task SetCusip_SameCusip_DoesNotPublish()
    {
        var stock = await SeedStock("037833100");

        await _sut.SetCusip(stock, "037833100");

        await _publishEndpoint
            .DidNotReceive()
            .Publish(Arg.Any<StockCusipChanged>(), Arg.Any<CancellationToken>());
    }
}
