using Equibles.CommonStocks.BusinessLogic;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Messaging.Contracts.CommonStocks;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Equibles.IntegrationTests.CommonStocks;

/// <summary>
/// Contract for recording a secondary listing's CUSIP (#4247). A row lands in
/// <see cref="CommonStockListedCusip"/> — NEVER in <see cref="CommonStockCusipAlias"/>,
/// which maps to the primary series and would collapse two securities into one row.
/// Admission: the ticker must be one of the stock's CURRENT secondary tickers, the CUSIP
/// must not be the stock's own primary, and a CUSIP already owned anywhere (a primary,
/// an alias, or another listing) keeps its first owner. Recording publishes the
/// stock-identity-changed signal so the holdings ledger re-imports history.
/// </summary>
public class CommonStockManagerRecordListedTickerCusipsTests
{
    private readonly CommonStockManager _sut;
    private readonly CommonStockRepository _repository;
    private readonly IBus _bus;

    public CommonStockManagerRecordListedTickerCusipsTests()
    {
        var context = TestDbContextFactory.Create(new CommonStocksModuleConfiguration());
        _repository = new CommonStockRepository(context);
        _bus = Substitute.For<IBus>();
        _sut = new CommonStockManager(_repository, _bus);
    }

    private async Task<CommonStock> SeedStock(
        string ticker,
        string cik,
        string cusip = null,
        List<string> secondaryTickers = null,
        bool active = true
    )
    {
        var stock = new CommonStock
        {
            Ticker = ticker,
            Name = $"{ticker} Test Co",
            Cik = cik,
            Cusip = cusip,
            SecondaryTickers = secondaryTickers ?? [],
            Active = active,
        };
        _repository.Add(stock);
        await _repository.SaveChanges();
        return stock;
    }

    [Fact]
    public async Task RecordListedTickerCusips_SiblingClassCusip_RecordsListingAndSignalsReimport()
    {
        // The Alphabet shape: GOOG's CUSIP recorded as a LISTING of the GOOGL filer.
        var stock = await SeedStock(
            "GOOGL",
            "1652044",
            cusip: "02079K305",
            secondaryTickers: ["GOOG"]
        );

        var recorded = await _sut.RecordListedTickerCusips(stock, [("GOOG", "02079K107")]);

        recorded.Should().Be(1);
        var listing = await _repository.GetListedCusips().SingleAsync();
        listing.CommonStockId.Should().Be(stock.Id);
        listing.ListedTicker.Should().Be("GOOG");
        listing.Cusip.Should().Be("02079K107");

        // The listing must never leak into the alias table — an alias maps to the
        // PRIMARY series, which is exactly the class collapse this table prevents.
        (await _repository.GetCusipAliases().AnyAsync())
            .Should()
            .BeFalse();

        // New identity means data sets already marked processed hold none of these
        // lines — the ledger-clear signal is what backfills them without a deploy.
        await _bus.Received(1)
            .Publish(
                Arg.Is<StockCusipChanged>(e => e.CommonStockId == stock.Id),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task RecordListedTickerCusips_TickerNotACurrentSecondary_RecordsNothing()
    {
        // Only the stock's own authoritative secondary list admits a listing; an arbitrary
        // symbol+CUSIP pair from the feed must not attach to this filer.
        var stock = await SeedStock("GOOGL", "1652044", cusip: "02079K305");

        var recorded = await _sut.RecordListedTickerCusips(stock, [("GOOG", "02079K107")]);

        recorded.Should().Be(0);
        (await _repository.GetListedCusips().AnyAsync()).Should().BeFalse();
        await _bus.DidNotReceive()
            .Publish(Arg.Any<StockCusipChanged>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordListedTickerCusips_StocksOwnPrimaryCusip_RecordsNothing()
    {
        // The primary CUSIP resolving through the listing table would tag primary-class
        // rows with a listed ticker; the primary's identity stays on the stock itself.
        var stock = await SeedStock(
            "GOOGL",
            "1652044",
            cusip: "02079K305",
            secondaryTickers: ["GOOG"]
        );

        var recorded = await _sut.RecordListedTickerCusips(stock, [("GOOG", "02079K305")]);

        recorded.Should().Be(0);
        (await _repository.GetListedCusips().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task RecordListedTickerCusips_CusipOwnedByInactivePrimary_FirstOwnerKeepsIt()
    {
        // One CUSIP identifies one security, ever. Whether the prior owner holds it as a
        // primary, an alias, or another listing, a later claim is dropped silently.
        var owner = await SeedStock("AAA", "0000000001", cusip: "111111111", active: false);
        var claimant = await SeedStock(
            "BBB",
            "0000000002",
            cusip: "222222222",
            secondaryTickers: ["BBB-A"]
        );

        var recorded = await _sut.RecordListedTickerCusips(claimant, [("BBB-A", "111111111")]);

        recorded.Should().Be(0);
        (await _repository.GetListedCusips().AnyAsync()).Should().BeFalse();
        await _bus.DidNotReceive()
            .Publish(Arg.Any<StockCusipChanged>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordRetiredCusipAliases_CusipOwnedByInactivePrimary_RecordsNothing()
    {
        await SeedStock("OLD", "0000000001", cusip: "111111111", active: false);
        var claimant = await SeedStock("LIVE", "0000000002", cusip: "222222222");

        var recorded = await _sut.RecordRetiredCusipAliases(claimant, ["111111111"]);

        recorded.Should().Be(0);
        (await _repository.GetCusipAliases().AnyAsync()).Should().BeFalse();
        await _bus.DidNotReceive()
            .Publish(Arg.Any<StockCusipChanged>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordListedTickerCusips_SameCusipOfferedTwice_RecordsOnce()
    {
        // The FTD archive repeats a (symbol, CUSIP) pair across many files; both the batch
        // dedupe and the unique index behind it must collapse the repeats.
        var stock = await SeedStock(
            "GOOGL",
            "1652044",
            cusip: "02079K305",
            secondaryTickers: ["GOOG"]
        );

        var first = await _sut.RecordListedTickerCusips(
            stock,
            [("GOOG", "02079K107"), ("GOOG", "02079k107")]
        );
        var second = await _sut.RecordListedTickerCusips(stock, [("GOOG", "02079K107")]);

        first.Should().Be(1);
        second.Should().Be(0);
        (await _repository.GetListedCusips().CountAsync()).Should().Be(1);
    }
}
