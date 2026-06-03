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

public class CommonStockManagerCreateWhitespaceTickerTests
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
    public async Task Create_WhitespaceOnlyTicker_ThrowsDomainValidationException()
    {
        // Contract (CommonStockManager.cs:116-118): "a whitespace-only value is not
        // a provided value. Ticker is the globally-unique key and the lookup key, so
        // accepting whitespace would corrupt the uniqueness invariant." A naive
        // IsNullOrEmpty check would let "   " through; the validator uses
        // IsNullOrWhiteSpace and must reject it before persisting. Existing Create
        // tests cover negative shares/market-cap but never the blank-required-field
        // rejection.
        var db = NewDb();
        var repo = Substitute.For<CommonStockRepository>(db);
        var sut = new CommonStockManager(repo, Substitute.For<IBus>());
        var stock = new CommonStock
        {
            Ticker = "   ",
            Name = "Test Corp",
            Cik = "0000099999",
        };

        var act = () => sut.Create(stock);

        (await act.Should().ThrowAsync<DomainValidationException>()).WithMessage("*Ticker*");
    }
}
