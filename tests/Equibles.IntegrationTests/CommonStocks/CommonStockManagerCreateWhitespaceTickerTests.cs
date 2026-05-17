using Equibles.CommonStocks.BusinessLogic;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Exceptions;
using Equibles.IntegrationTests.Helpers;
using MassTransit;
using NSubstitute;

namespace Equibles.IntegrationTests.CommonStocks;

public class CommonStockManagerCreateWhitespaceTickerTests
{
    private readonly CommonStockManager _sut;

    public CommonStockManagerCreateWhitespaceTickerTests()
    {
        var context = TestDbContextFactory.Create(new CommonStocksModuleConfiguration());
        _sut = new CommonStockManager(
            new CommonStockRepository(context),
            Substitute.For<IPublishEndpoint>()
        );
    }

    // Contract: "Ticker is required". Ticker is also the globally-unique key
    // and the lookup key (GetByPrimaryTicker), so a whitespace-only value is
    // not a provided ticker and must be rejected — persisting one corrupts the
    // uniqueness invariant and ticker lookups.
    [Fact]
    public async Task Create_WhitespaceOnlyTicker_IsRejected()
    {
        var stock = new CommonStock
        {
            Ticker = "   ",
            Name = "Whitespace Ticker Co",
            Cik = "0001234567",
        };

        var act = async () => await _sut.Create(stock);

        await act.Should().ThrowAsync<DomainValidationException>();
    }
}
