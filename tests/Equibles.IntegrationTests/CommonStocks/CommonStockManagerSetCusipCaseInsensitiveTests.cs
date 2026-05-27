using Equibles.CommonStocks.BusinessLogic;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Messaging.Contracts.CommonStocks;
using MassTransit;
using NSubstitute;

namespace Equibles.IntegrationTests.CommonStocks;

/// <summary>
/// SetCusip's no-op guard uses StringComparison.OrdinalIgnoreCase: CUSIPs are
/// 9-char alphanumeric (e.g. 38259P508), and a difference of letter case only
/// is the SAME identifier. The sibling SameCusip pin only uses identical case;
/// a case-only "change" wrongly publishing StockCusipChanged would trigger an
/// unnecessary Holdings 13F backfill. Pin: case-only diff is a true no-op.
/// </summary>
public class CommonStockManagerSetCusipCaseInsensitiveTests
{
    private readonly CommonStockManager _sut;
    private readonly IBus _publishEndpoint = Substitute.For<IBus>();
    private readonly CommonStockRepository _repository;

    public CommonStockManagerSetCusipCaseInsensitiveTests()
    {
        var context = TestDbContextFactory.Create(new CommonStocksModuleConfiguration());
        _repository = new CommonStockRepository(context);
        _sut = new CommonStockManager(_repository, _publishEndpoint);
    }

    [Fact]
    public async Task SetCusip_CusipDiffersOnlyByLetterCase_IsNoOpPublishesNothingAndKeepsStoredValue()
    {
        var stock = new CommonStock
        {
            Ticker = "GOOGL",
            Name = "Alphabet Inc",
            Cik = "0001652044",
            Cusip = "38259P508",
        };
        _repository.Add(stock);
        await _repository.SaveChanges();

        await _sut.SetCusip(stock, "38259p508");

        await _publishEndpoint
            .DidNotReceive()
            .Publish(Arg.Any<StockCusipChanged>(), Arg.Any<CancellationToken>());
        // Guard returns before mutation, so the stored CUSIP is untouched.
        stock.Cusip.Should().Be("38259P508");
    }
}
