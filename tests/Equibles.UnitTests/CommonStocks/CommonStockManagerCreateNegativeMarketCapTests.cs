using Equibles.CommonStocks.BusinessLogic;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Exceptions;
using Equibles.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Equibles.UnitTests.CommonStocks;

public class CommonStockManagerCreateNegativeMarketCapTests
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
    public async Task Create_NegativeMarketCapitalization_ThrowsDomainValidationException()
    {
        // Contract: MarketCapitalization is a monetary total and must never be
        // negative — the validator should reject it before persist.
        var db = NewDb();
        var repo = Substitute.For<CommonStockRepository>(db);
        var sut = new CommonStockManager(repo, Substitute.For<IPublishEndpoint>());
        var stock = new CommonStock
        {
            Ticker = "TEST",
            Name = "Test Corp",
            Cik = "0000099999",
            MarketCapitalization = -1,
        };

        var act = () => sut.Create(stock);

        (await act.Should().ThrowAsync<DomainValidationException>()).WithMessage(
            "*MarketCapitalization*"
        );
    }
}
