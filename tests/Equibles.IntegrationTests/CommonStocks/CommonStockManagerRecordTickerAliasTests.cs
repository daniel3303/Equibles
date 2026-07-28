using Equibles.CommonStocks.BusinessLogic;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Equibles.IntegrationTests.CommonStocks;

/// <summary>
/// Contract for the ticker-rename redirect map: a retired primary ticker is recorded so the
/// old symbol can 301 to the current one; an alias never shadows a live primary or secondary
/// ticker; and — unlike the CUSIP alias's first-owner-keeps rule — a recycled symbol belongs
/// to its most recent holder (last-writer-wins), because exchanges reassign tickers across
/// unrelated issuers. RecordTickerAlias only STAGES: the caller (the SEC sync mid-rename)
/// owns the SaveChanges so alias and rename commit atomically.
/// </summary>
public class CommonStockManagerRecordTickerAliasTests
{
    private readonly CommonStockManager _sut;
    private readonly CommonStockRepository _repository;

    public CommonStockManagerRecordTickerAliasTests()
    {
        var context = TestDbContextFactory.Create(new CommonStocksModuleConfiguration());
        _repository = new CommonStockRepository(context);
        _sut = new CommonStockManager(_repository, Substitute.For<IBus>());
    }

    private async Task<CommonStock> SeedStock(
        string ticker,
        string cik,
        List<string> secondaryTickers = null
    )
    {
        var stock = new CommonStock
        {
            Ticker = ticker,
            Name = $"{ticker} Test Co",
            Cik = cik,
            SecondaryTickers = secondaryTickers ?? [],
        };
        _repository.Add(stock);
        await _repository.SaveChanges();
        return stock;
    }

    // The LendingClub shape: LC → HAPN records LC as this stock's alias.
    [Fact]
    public async Task RecordTickerAlias_RetiredTicker_StagesAliasForTheStock()
    {
        var stock = await SeedStock("HAPN", "1409970");

        var staged = await _sut.RecordTickerAlias(stock, "LC");
        await _repository.SaveChanges();

        staged.Should().NotBeNull();
        var alias = await _repository.GetTickerAliases().SingleAsync();
        alias.Ticker.Should().Be("LC");
        alias.CommonStockId.Should().Be(stock.Id);
    }

    // THE PRODUCTION ORDERING — the sync calls this MID-RENAME: the stock's row in the
    // database still holds the retired symbol, the new ticker exists only on the in-memory
    // entity, unflushed. The live-holder guard queries the database, so without excluding
    // the stock itself it matches this very row and stages nothing — which shipped the
    // feature as a silent no-op on the only path that ever calls it. The test above seeds
    // the stock already saved under the NEW symbol, which is exactly how that was missed.
    [Fact]
    public async Task RecordTickerAlias_RenameStagedInMemoryOnly_StillStagesTheAlias()
    {
        var stock = await SeedStock("LC", "1409970");
        stock.Ticker = "HAPN"; // in-memory only — the database row still says LC

        var staged = await _sut.RecordTickerAlias(stock, "LC");
        await _repository.SaveChanges();

        staged.Should().NotBeNull();
        var alias = await _repository.GetTickerAliases().SingleAsync();
        alias.Ticker.Should().Be("LC");
        alias.CommonStockId.Should().Be(stock.Id);
    }

    // Re-adoption cleanup (the deletion half of last-writer-wins): renaming BACK to a symbol
    // that sits in the alias map deletes that alias — once the symbol is live again the row
    // is at best shadowed, at worst a wrong redirect after the next rename.
    [Fact]
    public async Task RecordTickerAlias_RenamingOntoAnAliasedSymbol_DeletesTheStaleAlias()
    {
        var stock = await SeedStock("HAPN", "1409970");
        await _sut.RecordTickerAlias(stock, "LC");
        await _repository.SaveChanges();

        // The round trip: HAPN → LC again (rename staged in memory, mid-sync).
        stock.Ticker = "LC";
        await _sut.RecordTickerAlias(stock, "HAPN");
        await _repository.SaveChanges();

        var aliases = await _repository.GetTickerAliases().ToListAsync();
        aliases.Should().ContainSingle().Which.Ticker.Should().Be("HAPN");
    }

    // A symbol the stock keeps as a SECONDARY listing after the primary moves is not a
    // retirement — the live lookup still resolves it, so no alias is staged.
    [Fact]
    public async Task RecordTickerAlias_SymbolKeptAsOwnSecondary_StagesNothing()
    {
        var stock = await SeedStock("ABLZF", "1091587", ["ABBNY"]);

        var staged = await _sut.RecordTickerAlias(stock, "ABBNY");

        staged.Should().BeNull();
        (await _repository.GetTickerAliases().AnyAsync()).Should().BeFalse();
    }

    // Lowercase input normalizes to the stored uppercase form — the unique index must never
    // hold two case-variants of one symbol.
    [Fact]
    public async Task RecordTickerAlias_LowercaseInput_StoresUppercase()
    {
        var stock = await SeedStock("HAPN", "1409970");

        await _sut.RecordTickerAlias(stock, "lc");
        await _repository.SaveChanges();

        (await _repository.GetTickerAliases().SingleAsync()).Ticker.Should().Be("LC");
    }

    // A symbol some OTHER stock currently trades under (primary) is live — recording it
    // would seed a redirect that only ever fires wrongly, after that holder renames.
    [Fact]
    public async Task RecordTickerAlias_SymbolIsAnotherStocksLivePrimary_StagesNothing()
    {
        var renamed = await SeedStock("HAPN", "1409970");
        await SeedStock("LC", "9999999");

        var staged = await _sut.RecordTickerAlias(renamed, "LC");

        staged.Should().BeNull();
        (await _repository.GetTickerAliases().AnyAsync()).Should().BeFalse();
    }

    // Same for a live SECONDARY ticker — those resolve on every stock route too.
    [Fact]
    public async Task RecordTickerAlias_SymbolIsAnotherStocksLiveSecondary_StagesNothing()
    {
        var renamed = await SeedStock("HAPN", "1409970");
        await SeedStock("ABLZF", "1091587", ["LC"]);

        var staged = await _sut.RecordTickerAlias(renamed, "LC");

        staged.Should().BeNull();
        (await _repository.GetTickerAliases().AnyAsync()).Should().BeFalse();
    }

    // Last-writer-wins: the symbol was already an alias of a DIFFERENT stock (an earlier
    // holder retired it). The most recent holder owns the redirect, so the stale row is
    // replaced — the opposite of SetCusip's first-owner-keeps rule, deliberately.
    [Fact]
    public async Task RecordTickerAlias_SymbolAliasedToAnotherStock_ReassignsToTheNewHolder()
    {
        var earlierHolder = await SeedStock("NEWCO", "1111111");
        await _sut.RecordTickerAlias(earlierHolder, "XYZ");
        await _repository.SaveChanges();

        var laterHolder = await SeedStock("LATER", "2222222");
        await _sut.RecordTickerAlias(laterHolder, "XYZ");
        await _repository.SaveChanges();

        var alias = await _repository.GetTickerAliases().SingleAsync(a => a.Ticker == "XYZ");
        alias.CommonStockId.Should().Be(laterHolder.Id);
    }

    // Re-retiring a symbol the stock already has an alias for is a no-op, not a duplicate.
    [Fact]
    public async Task RecordTickerAlias_AlreadyAliasedToSameStock_StagesNothing()
    {
        var stock = await SeedStock("HAPN", "1409970");
        await _sut.RecordTickerAlias(stock, "LC");
        await _repository.SaveChanges();

        var staged = await _sut.RecordTickerAlias(stock, "LC");
        await _repository.SaveChanges();

        staged.Should().BeNull();
        (await _repository.GetTickerAliases().CountAsync()).Should().Be(1);
    }

    // Guard rails: blank input and "retiring" the stock's own current ticker stage nothing.
    [Fact]
    public async Task RecordTickerAlias_BlankOrCurrentTicker_StagesNothing()
    {
        var stock = await SeedStock("HAPN", "1409970");

        (await _sut.RecordTickerAlias(stock, null)).Should().BeNull();
        (await _sut.RecordTickerAlias(stock, "  ")).Should().BeNull();
        (await _sut.RecordTickerAlias(stock, "hapn")).Should().BeNull();
        (await _repository.GetTickerAliases().AnyAsync()).Should().BeFalse();
    }
}
