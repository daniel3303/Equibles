using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Congress.Data;
using Equibles.Congress.Data.Models;
using Equibles.Congress.Repositories;
using Equibles.Data;
using Equibles.Tests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Tests.Congress;

public class CongressMemberRepositoryTests : IDisposable {
    private readonly EquiblesDbContext _dbContext;
    private readonly CongressMemberRepository _repository;

    public CongressMemberRepositoryTests() {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new CongressModuleConfiguration());
        _repository = new CongressMemberRepository(_dbContext);
    }

    public void Dispose() {
        _dbContext.Dispose();
    }

    private static CongressMember CreateMember(
        string name = "Nancy Pelosi",
        CongressPosition position = CongressPosition.Representative) {
        return new CongressMember {
            Id = Guid.NewGuid(),
            Name = name,
            Position = position,
        };
    }

    // ── GetByName ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetByName_ExactMatch_ReturnsMember() {
        var member = CreateMember("Nancy Pelosi");
        _dbContext.Set<CongressMember>().Add(member);
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByName("Nancy Pelosi");

        result.Should().NotBeNull();
        result.Id.Should().Be(member.Id);
        result.Name.Should().Be("Nancy Pelosi");
    }

    [Fact]
    public async Task GetByName_NonExistentName_ReturnsNull() {
        var member = CreateMember("Nancy Pelosi");
        _dbContext.Set<CongressMember>().Add(member);
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByName("John Doe");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByName_CaseSensitive_ReturnsNullForWrongCase() {
        var member = CreateMember("Nancy Pelosi");
        _dbContext.Set<CongressMember>().Add(member);
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByName("nancy pelosi");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByName_EmptyDatabase_ReturnsNull() {
        var result = await _repository.GetByName("Nancy Pelosi");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByName_MultipleMembersExist_ReturnsCorrectOne() {
        _dbContext.Set<CongressMember>().AddRange(
            CreateMember("Nancy Pelosi", CongressPosition.Representative),
            CreateMember("Dan Crenshaw", CongressPosition.Representative),
            CreateMember("Tommy Tuberville", CongressPosition.Senator));
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByName("Tommy Tuberville");

        result.Should().NotBeNull();
        result.Name.Should().Be("Tommy Tuberville");
        result.Position.Should().Be(CongressPosition.Senator);
    }

    // ── Search ─────────────────────────────────────────────────────────
    // Search uses EF.Functions.ILike which requires a relational provider
    // (PostgreSQL). The in-memory provider does not support ILike, so we
    // verify the method constructs a queryable that throws on evaluation.
    // Full integration tests with a real database should cover the actual
    // pattern-matching behavior.

    [Fact]
    public void Search_ReturnsQueryable() {
        var queryable = _repository.Search("Pelosi");

        queryable.Should().BeAssignableTo<IQueryable<CongressMember>>();
    }

    [Fact]
    public void Search_InMemoryProvider_ThrowsOnEvaluation() {
        _dbContext.Set<CongressMember>().Add(CreateMember("Nancy Pelosi"));
        _dbContext.SaveChanges();

        var queryable = _repository.Search("Pelosi");

        var act = () => queryable.ToList();
        act.Should().Throw<InvalidOperationException>();
    }

    // ── Inherited BaseRepository methods ───────────────────────────────

    [Fact]
    public async Task Get_ExistingMember_ReturnsMember() {
        var member = CreateMember();
        _dbContext.Set<CongressMember>().Add(member);
        await _dbContext.SaveChangesAsync();

        var result = await _repository.Get(member.Id);

        result.Should().NotBeNull();
        result.Id.Should().Be(member.Id);
    }

    [Fact]
    public void GetAll_ReturnsMembersAsQueryable() {
        _dbContext.Set<CongressMember>().AddRange(
            CreateMember("Nancy Pelosi"),
            CreateMember("Dan Crenshaw"));
        _dbContext.SaveChanges();

        var result = _repository.GetAll().ToList();

        result.Should().HaveCount(2);
    }
}

public class CongressionalTradeRepositoryTests : IDisposable {
    private readonly EquiblesDbContext _dbContext;
    private readonly CongressionalTradeRepository _repository;

    public CongressionalTradeRepositoryTests() {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new CongressModuleConfiguration());
        _repository = new CongressionalTradeRepository(_dbContext);
    }

    public void Dispose() {
        _dbContext.Dispose();
    }

    private static CommonStock CreateStock(string ticker = "AAPL", string name = "Apple Inc.") {
        return new CommonStock {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = name,
        };
    }

    private static CongressMember CreateMember(
        string name = "Nancy Pelosi",
        CongressPosition position = CongressPosition.Representative) {
        return new CongressMember {
            Id = Guid.NewGuid(),
            Name = name,
            Position = position,
        };
    }

    private static CongressionalTrade CreateTrade(
        CongressMember member,
        CommonStock stock,
        DateOnly? transactionDate = null,
        DateOnly? filingDate = null,
        CongressTransactionType type = CongressTransactionType.Purchase,
        string assetName = "Common Stock",
        long amountFrom = 1_001,
        long amountTo = 15_000) {
        var txDate = transactionDate ?? new DateOnly(2024, 6, 15);
        return new CongressionalTrade {
            Id = Guid.NewGuid(),
            CongressMemberId = member.Id,
            CongressMember = member,
            CommonStockId = stock.Id,
            CommonStock = stock,
            TransactionDate = txDate,
            FilingDate = filingDate ?? txDate.AddDays(30),
            TransactionType = type,
            OwnerType = "Self",
            AssetName = assetName,
            AmountFrom = amountFrom,
            AmountTo = amountTo,
        };
    }

    private async Task<(CommonStock apple, CommonStock msft, CongressMember pelosi, CongressMember tuberville)> SeedStandardData() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        var msft = CreateStock("MSFT", "Microsoft Corp.");
        var pelosi = CreateMember("Nancy Pelosi", CongressPosition.Representative);
        var tuberville = CreateMember("Tommy Tuberville", CongressPosition.Senator);

        _dbContext.Set<CommonStock>().AddRange(apple, msft);
        _dbContext.Set<CongressMember>().AddRange(pelosi, tuberville);
        await _dbContext.SaveChangesAsync();

        return (apple, msft, pelosi, tuberville);
    }

    // ── GetByStock (CommonStock) ───────────────────────────────────────

    [Fact]
    public async Task GetByStock_ReturnsTradesForStock() {
        var (apple, msft, pelosi, tuberville) = await SeedStandardData();

        var appleTrade1 = CreateTrade(pelosi, apple, new DateOnly(2024, 3, 1));
        var appleTrade2 = CreateTrade(tuberville, apple, new DateOnly(2024, 4, 1));
        var msftTrade = CreateTrade(pelosi, msft, new DateOnly(2024, 3, 15));

        _dbContext.Set<CongressionalTrade>().AddRange(appleTrade1, appleTrade2, msftTrade);
        await _dbContext.SaveChangesAsync();

        var result = _repository.GetByStock(apple).ToList();

        result.Should().HaveCount(2);
        result.Should().AllSatisfy(t => t.CommonStockId.Should().Be(apple.Id));
    }

    [Fact]
    public async Task GetByStock_StockWithNoTrades_ReturnsEmpty() {
        var (apple, msft, pelosi, _) = await SeedStandardData();

        _dbContext.Set<CongressionalTrade>().Add(
            CreateTrade(pelosi, apple, new DateOnly(2024, 3, 1)));
        await _dbContext.SaveChangesAsync();

        var result = _repository.GetByStock(msft).ToList();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByStock_EmptyDatabase_ReturnsEmpty() {
        var stock = CreateStock();
        _dbContext.Set<CommonStock>().Add(stock);
        await _dbContext.SaveChangesAsync();

        var result = _repository.GetByStock(stock).ToList();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByStock_ReturnsQueryable() {
        var (apple, _, _, _) = await SeedStandardData();

        var result = _repository.GetByStock(apple);

        result.Should().BeAssignableTo<IQueryable<CongressionalTrade>>();
    }

    // ── GetByStock (CommonStock, DateOnly from, DateOnly to) ───────────

    [Fact]
    public async Task GetByStock_WithDateRange_FiltersCorrectly() {
        var (apple, _, pelosi, tuberville) = await SeedStandardData();

        var jan = CreateTrade(pelosi, apple, new DateOnly(2024, 1, 15));
        var mar = CreateTrade(pelosi, apple, new DateOnly(2024, 3, 15), assetName: "AAPL Options");
        var jun = CreateTrade(tuberville, apple, new DateOnly(2024, 6, 15));
        var sep = CreateTrade(pelosi, apple, new DateOnly(2024, 9, 15), assetName: "AAPL Calls");

        _dbContext.Set<CongressionalTrade>().AddRange(jan, mar, jun, sep);
        await _dbContext.SaveChangesAsync();

        var result = _repository
            .GetByStock(apple, new DateOnly(2024, 2, 1), new DateOnly(2024, 7, 1))
            .ToList();

        result.Should().HaveCount(2);
        result.Should().Contain(t => t.Id == mar.Id);
        result.Should().Contain(t => t.Id == jun.Id);
    }

    [Fact]
    public async Task GetByStock_WithDateRange_IncludesBoundaryDates() {
        var (apple, _, pelosi, _) = await SeedStandardData();

        var fromDate = new DateOnly(2024, 3, 1);
        var toDate = new DateOnly(2024, 3, 31);

        var onFrom = CreateTrade(pelosi, apple, fromDate, assetName: "On From");
        var onTo = CreateTrade(pelosi, apple, toDate, assetName: "On To");
        var middle = CreateTrade(pelosi, apple, new DateOnly(2024, 3, 15), assetName: "Middle");

        _dbContext.Set<CongressionalTrade>().AddRange(onFrom, onTo, middle);
        await _dbContext.SaveChangesAsync();

        var result = _repository.GetByStock(apple, fromDate, toDate).ToList();

        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetByStock_WithDateRange_ExcludesOutOfRangeTrades() {
        var (apple, _, pelosi, _) = await SeedStandardData();

        var before = CreateTrade(pelosi, apple, new DateOnly(2024, 1, 1), assetName: "Before");
        var after = CreateTrade(pelosi, apple, new DateOnly(2024, 12, 31), assetName: "After");

        _dbContext.Set<CongressionalTrade>().AddRange(before, after);
        await _dbContext.SaveChangesAsync();

        var result = _repository
            .GetByStock(apple, new DateOnly(2024, 3, 1), new DateOnly(2024, 6, 30))
            .ToList();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByStock_WithDateRange_DoesNotIncludeOtherStocks() {
        var (apple, msft, pelosi, _) = await SeedStandardData();

        var appleTrade = CreateTrade(pelosi, apple, new DateOnly(2024, 3, 15));
        var msftTrade = CreateTrade(pelosi, msft, new DateOnly(2024, 3, 15));

        _dbContext.Set<CongressionalTrade>().AddRange(appleTrade, msftTrade);
        await _dbContext.SaveChangesAsync();

        var result = _repository
            .GetByStock(apple, new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31))
            .ToList();

        result.Should().ContainSingle()
            .Which.Id.Should().Be(appleTrade.Id);
    }

    [Fact]
    public async Task GetByStock_WithDateRange_EmptyRange_ReturnsEmpty() {
        var (apple, _, pelosi, _) = await SeedStandardData();

        _dbContext.Set<CongressionalTrade>().Add(
            CreateTrade(pelosi, apple, new DateOnly(2024, 6, 15)));
        await _dbContext.SaveChangesAsync();

        var result = _repository
            .GetByStock(apple, new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 1))
            .ToList();

        result.Should().BeEmpty();
    }

    // ── GetByMember ────────────────────────────────────────────────────

    [Fact]
    public async Task GetByMember_ReturnsTradesForMember() {
        var (apple, msft, pelosi, tuberville) = await SeedStandardData();

        var pelosiTrade1 = CreateTrade(pelosi, apple, new DateOnly(2024, 3, 1));
        var pelosiTrade2 = CreateTrade(pelosi, msft, new DateOnly(2024, 4, 1));
        var tubervilleTrade = CreateTrade(tuberville, apple, new DateOnly(2024, 5, 1));

        _dbContext.Set<CongressionalTrade>().AddRange(pelosiTrade1, pelosiTrade2, tubervilleTrade);
        await _dbContext.SaveChangesAsync();

        var result = _repository.GetByMember(pelosi).ToList();

        result.Should().HaveCount(2);
        result.Should().AllSatisfy(t => t.CongressMemberId.Should().Be(pelosi.Id));
    }

    [Fact]
    public async Task GetByMember_MemberWithNoTrades_ReturnsEmpty() {
        var (apple, _, pelosi, tuberville) = await SeedStandardData();

        _dbContext.Set<CongressionalTrade>().Add(
            CreateTrade(pelosi, apple, new DateOnly(2024, 3, 1)));
        await _dbContext.SaveChangesAsync();

        var result = _repository.GetByMember(tuberville).ToList();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByMember_ReturnsQueryable() {
        var (_, _, pelosi, _) = await SeedStandardData();

        var result = _repository.GetByMember(pelosi);

        result.Should().BeAssignableTo<IQueryable<CongressionalTrade>>();
    }

    [Fact]
    public async Task GetByMember_MultipleStocks_ReturnsAll() {
        var (apple, msft, pelosi, _) = await SeedStandardData();
        var goog = CreateStock("GOOG", "Alphabet Inc.");
        _dbContext.Set<CommonStock>().Add(goog);
        await _dbContext.SaveChangesAsync();

        _dbContext.Set<CongressionalTrade>().AddRange(
            CreateTrade(pelosi, apple, new DateOnly(2024, 1, 10)),
            CreateTrade(pelosi, msft, new DateOnly(2024, 2, 20)),
            CreateTrade(pelosi, goog, new DateOnly(2024, 3, 30)));
        await _dbContext.SaveChangesAsync();

        var result = _repository.GetByMember(pelosi).ToList();

        result.Should().HaveCount(3);
    }

    // ── Inherited BaseRepository methods ───────────────────────────────

    [Fact]
    public async Task Get_ExistingTrade_ReturnsTrade() {
        var (apple, _, pelosi, _) = await SeedStandardData();

        var trade = CreateTrade(pelosi, apple);
        _dbContext.Set<CongressionalTrade>().Add(trade);
        await _dbContext.SaveChangesAsync();

        var result = await _repository.Get(trade.Id);

        result.Should().NotBeNull();
        result.Id.Should().Be(trade.Id);
    }

    [Fact]
    public async Task GetAll_ReturnsAllTrades() {
        var (apple, msft, pelosi, tuberville) = await SeedStandardData();

        _dbContext.Set<CongressionalTrade>().AddRange(
            CreateTrade(pelosi, apple, new DateOnly(2024, 1, 1)),
            CreateTrade(tuberville, msft, new DateOnly(2024, 2, 1)),
            CreateTrade(pelosi, msft, new DateOnly(2024, 3, 1)));
        await _dbContext.SaveChangesAsync();

        var result = _repository.GetAll().ToList();

        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task Add_PersistsTradeAfterSave() {
        var (apple, _, pelosi, _) = await SeedStandardData();

        var trade = CreateTrade(pelosi, apple);
        _repository.Add(trade);
        await _repository.SaveChanges();

        _repository.ClearChangeTracker();
        var persisted = await _repository.Get(trade.Id);
        persisted.Should().NotBeNull();
        persisted.TransactionType.Should().Be(CongressTransactionType.Purchase);
    }

    [Fact]
    public async Task Delete_RemovesTradeFromDatabase() {
        var (apple, _, pelosi, _) = await SeedStandardData();

        var trade = CreateTrade(pelosi, apple);
        _dbContext.Set<CongressionalTrade>().Add(trade);
        await _dbContext.SaveChangesAsync();

        _repository.Delete(trade);
        await _repository.SaveChanges();

        _dbContext.Set<CongressionalTrade>().Should().BeEmpty();
    }

    // ── Trade property verification ────────────────────────────────────

    [Fact]
    public async Task Trade_PersistsAllProperties() {
        var (apple, _, pelosi, _) = await SeedStandardData();

        var trade = CreateTrade(
            pelosi, apple,
            transactionDate: new DateOnly(2024, 5, 10),
            filingDate: new DateOnly(2024, 6, 9),
            type: CongressTransactionType.Sale,
            assetName: "AAPL Call Options",
            amountFrom: 15_001,
            amountTo: 50_000);
        trade.OwnerType = "Spouse";

        _dbContext.Set<CongressionalTrade>().Add(trade);
        await _dbContext.SaveChangesAsync();

        _repository.ClearChangeTracker();
        var persisted = await _repository.Get(trade.Id);

        persisted.Should().NotBeNull();
        persisted.CongressMemberId.Should().Be(pelosi.Id);
        persisted.CommonStockId.Should().Be(apple.Id);
        persisted.TransactionDate.Should().Be(new DateOnly(2024, 5, 10));
        persisted.FilingDate.Should().Be(new DateOnly(2024, 6, 9));
        persisted.TransactionType.Should().Be(CongressTransactionType.Sale);
        persisted.OwnerType.Should().Be("Spouse");
        persisted.AssetName.Should().Be("AAPL Call Options");
        persisted.AmountFrom.Should().Be(15_001);
        persisted.AmountTo.Should().Be(50_000);
    }
}
