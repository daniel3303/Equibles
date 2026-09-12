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
        EquityIssuerRepository repo = Substitute.For<EquityIssuerRepository>(db);
        EquityIdentityManager sut = new EquityIdentityManager(repo, Substitute.For<IBus>());
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "TEST",
            Name: "Test Corp",
            Cik: "0000099999",
            MarketCapitalization: -1
        );

        var act = () => sut.Create(stock);

        (await act.Should().ThrowAsync<DomainValidationException>()).WithMessage(
            "*MarketCapitalization*"
        );
    }
}
