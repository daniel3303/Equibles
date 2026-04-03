using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.InsiderTrading.Data;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Repositories;
using Equibles.Tests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Tests.InsiderTrading;

public class InsiderOwnerRepositoryTests : IDisposable {
    private readonly EquiblesDbContext _dbContext;
    private readonly InsiderOwnerRepository _repository;

    public InsiderOwnerRepositoryTests() {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new InsiderTradingModuleConfiguration());
        _repository = new InsiderOwnerRepository(_dbContext);
    }

    public void Dispose() {
        _dbContext.Dispose();
    }

    private static InsiderOwner CreateOwner(
        string cik = "0001234567",
        string name = "John Doe",
        string city = "New York",
        string state = "NY",
        bool isDirector = true,
        bool isOfficer = false,
        string officerTitle = null,
        bool isTenPercentOwner = false) {
        return new InsiderOwner {
            Id = Guid.NewGuid(),
            OwnerCik = cik,
            Name = name,
            City = city,
            StateOrCountry = state,
            IsDirector = isDirector,
            IsOfficer = isOfficer,
            OfficerTitle = officerTitle,
            IsTenPercentOwner = isTenPercentOwner,
        };
    }

    // ── GetByOwnerCik ──────────────────────────────────────────────────

    [Fact]
    public async Task GetByOwnerCik_ExistingCik_ReturnsOwner() {
        var owner = CreateOwner(cik: "0001111111", name: "Alice Smith");
        _repository.Add(owner);
        await _repository.SaveChanges();

        var result = await _repository.GetByOwnerCik("0001111111");

        result.Should().NotBeNull();
        result.Id.Should().Be(owner.Id);
        result.Name.Should().Be("Alice Smith");
        result.OwnerCik.Should().Be("0001111111");
    }

    [Fact]
    public async Task GetByOwnerCik_NonExistentCik_ReturnsNull() {
        var owner = CreateOwner(cik: "0001111111");
        _repository.Add(owner);
        await _repository.SaveChanges();

        var result = await _repository.GetByOwnerCik("9999999999");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByOwnerCik_EmptyDatabase_ReturnsNull() {
        var result = await _repository.GetByOwnerCik("0001111111");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByOwnerCik_MultipleOwners_ReturnsCorrectOne() {
        var owner1 = CreateOwner(cik: "0001111111", name: "Alice");
        var owner2 = CreateOwner(cik: "0002222222", name: "Bob");
        var owner3 = CreateOwner(cik: "0003333333", name: "Charlie");
        _repository.AddRange([owner1, owner2, owner3]);
        await _repository.SaveChanges();

        var result = await _repository.GetByOwnerCik("0002222222");

        result.Should().NotBeNull();
        result.Name.Should().Be("Bob");
    }

    // ── GetByOwnerCiks ─────────────────────────────────────────────────

    [Fact]
    public async Task GetByOwnerCiks_MatchingCiks_ReturnsMatchingOwners() {
        var owner1 = CreateOwner(cik: "0001111111", name: "Alice");
        var owner2 = CreateOwner(cik: "0002222222", name: "Bob");
        var owner3 = CreateOwner(cik: "0003333333", name: "Charlie");
        _repository.AddRange([owner1, owner2, owner3]);
        await _repository.SaveChanges();

        var result = await _repository
            .GetByOwnerCiks(["0001111111", "0003333333"])
            .ToListAsync();

        result.Should().HaveCount(2);
        result.Select(o => o.Name).Should().Contain("Alice").And.Contain("Charlie");
        result.Select(o => o.Name).Should().NotContain("Bob");
    }

    [Fact]
    public async Task GetByOwnerCiks_NoneMatching_ReturnsEmpty() {
        var owner = CreateOwner(cik: "0001111111");
        _repository.Add(owner);
        await _repository.SaveChanges();

        var result = await _repository
            .GetByOwnerCiks(["9999999999", "8888888888"])
            .ToListAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByOwnerCiks_EmptyInput_ReturnsEmpty() {
        var owner = CreateOwner(cik: "0001111111");
        _repository.Add(owner);
        await _repository.SaveChanges();

        var result = await _repository
            .GetByOwnerCiks([])
            .ToListAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByOwnerCiks_ReturnsQueryable() {
        var owner = CreateOwner(cik: "0001111111");
        _repository.Add(owner);
        await _repository.SaveChanges();

        var result = _repository.GetByOwnerCiks(["0001111111"]);

        result.Should().BeAssignableTo<IQueryable<InsiderOwner>>();
    }

    // ── Search ─────────────────────────────────────────────────────────
    // Note: EF.Functions.ILike is not supported by the in-memory provider,
    // so Search tests require a real database and are excluded here.
}

public class InsiderTransactionRepositoryTests : IDisposable {
    private readonly EquiblesDbContext _dbContext;
    private readonly InsiderTransactionRepository _repository;

    public InsiderTransactionRepositoryTests() {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new InsiderTradingModuleConfiguration());
        _repository = new InsiderTransactionRepository(_dbContext);
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

    private static InsiderOwner CreateOwner(string cik = "0001234567", string name = "John Doe") {
        return new InsiderOwner {
            Id = Guid.NewGuid(),
            OwnerCik = cik,
            Name = name,
            City = "New York",
            StateOrCountry = "NY",
            IsDirector = true,
        };
    }

    private static InsiderTransaction CreateTransaction(
        CommonStock stock,
        InsiderOwner owner,
        DateOnly? filingDate = null,
        DateOnly? transactionDate = null,
        TransactionCode code = TransactionCode.Purchase,
        long shares = 1000,
        decimal pricePerShare = 150.00m,
        AcquiredDisposed acquiredDisposed = AcquiredDisposed.Acquired,
        long sharesOwnedAfter = 5000,
        OwnershipNature ownershipNature = OwnershipNature.Direct,
        string securityTitle = "Common Stock",
        string accessionNumber = "0001234567-24-000001",
        bool isAmendment = false) {
        return new InsiderTransaction {
            Id = Guid.NewGuid(),
            CommonStockId = stock.Id,
            CommonStock = stock,
            InsiderOwnerId = owner.Id,
            InsiderOwner = owner,
            FilingDate = filingDate ?? new DateOnly(2024, 6, 15),
            TransactionDate = transactionDate ?? new DateOnly(2024, 6, 14),
            TransactionCode = code,
            Shares = shares,
            PricePerShare = pricePerShare,
            AcquiredDisposed = acquiredDisposed,
            SharesOwnedAfter = sharesOwnedAfter,
            OwnershipNature = ownershipNature,
            SecurityTitle = securityTitle,
            AccessionNumber = accessionNumber,
            IsAmendment = isAmendment,
        };
    }

    private async Task SeedStockAndOwner(CommonStock stock, InsiderOwner owner) {
        _dbContext.Set<CommonStock>().Add(stock);
        _dbContext.Set<InsiderOwner>().Add(owner);
        await _dbContext.SaveChangesAsync();
    }

    // ── GetByStock ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetByStock_StockWithTransactions_ReturnsAll() {
        var stock = CreateStock();
        var owner = CreateOwner();
        await SeedStockAndOwner(stock, owner);

        var tx1 = CreateTransaction(stock, owner, accessionNumber: "0001-24-000001");
        var tx2 = CreateTransaction(stock, owner,
            transactionDate: new DateOnly(2024, 7, 1),
            accessionNumber: "0001-24-000002");
        _repository.AddRange([tx1, tx2]);
        await _repository.SaveChanges();

        var result = await _repository.GetByStock(stock).ToListAsync();

        result.Should().HaveCount(2);
        result.Select(t => t.Id).Should().Contain(tx1.Id).And.Contain(tx2.Id);
    }

    [Fact]
    public async Task GetByStock_StockWithNoTransactions_ReturnsEmpty() {
        var stock = CreateStock();
        var owner = CreateOwner();
        await SeedStockAndOwner(stock, owner);

        var result = await _repository.GetByStock(stock).ToListAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByStock_DoesNotReturnTransactionsForOtherStocks() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        var msft = CreateStock("MSFT", "Microsoft Corp.");
        var owner = CreateOwner();
        _dbContext.Set<CommonStock>().AddRange(apple, msft);
        _dbContext.Set<InsiderOwner>().Add(owner);
        await _dbContext.SaveChangesAsync();

        var appleTx = CreateTransaction(apple, owner, accessionNumber: "0001-24-000001");
        var msftTx = CreateTransaction(msft, owner, accessionNumber: "0001-24-000002");
        _repository.AddRange([appleTx, msftTx]);
        await _repository.SaveChanges();

        var result = await _repository.GetByStock(apple).ToListAsync();

        result.Should().ContainSingle()
            .Which.Id.Should().Be(appleTx.Id);
    }

    [Fact]
    public async Task GetByStock_ReturnsQueryable() {
        var stock = CreateStock();
        var owner = CreateOwner();
        await SeedStockAndOwner(stock, owner);

        var result = _repository.GetByStock(stock);

        result.Should().BeAssignableTo<IQueryable<InsiderTransaction>>();
    }

    // ── GetByStock (date range overload) ───────────────────────────────

    [Fact]
    public async Task GetByStock_DateRange_ReturnsOnlyTransactionsInRange() {
        var stock = CreateStock();
        var owner = CreateOwner();
        await SeedStockAndOwner(stock, owner);

        var txBefore = CreateTransaction(stock, owner,
            transactionDate: new DateOnly(2024, 1, 10),
            accessionNumber: "0001-24-000001");
        var txInRange1 = CreateTransaction(stock, owner,
            transactionDate: new DateOnly(2024, 3, 15),
            accessionNumber: "0001-24-000002");
        var txInRange2 = CreateTransaction(stock, owner,
            transactionDate: new DateOnly(2024, 5, 20),
            accessionNumber: "0001-24-000003");
        var txAfter = CreateTransaction(stock, owner,
            transactionDate: new DateOnly(2024, 8, 25),
            accessionNumber: "0001-24-000004");
        _repository.AddRange([txBefore, txInRange1, txInRange2, txAfter]);
        await _repository.SaveChanges();

        var from = new DateOnly(2024, 3, 1);
        var to = new DateOnly(2024, 6, 30);
        var result = await _repository.GetByStock(stock, from, to).ToListAsync();

        result.Should().HaveCount(2);
        result.Select(t => t.Id).Should().Contain(txInRange1.Id).And.Contain(txInRange2.Id);
    }

    [Fact]
    public async Task GetByStock_DateRange_IncludesBoundaryDates() {
        var stock = CreateStock();
        var owner = CreateOwner();
        await SeedStockAndOwner(stock, owner);

        var fromDate = new DateOnly(2024, 3, 1);
        var toDate = new DateOnly(2024, 3, 31);
        var txOnFrom = CreateTransaction(stock, owner,
            transactionDate: fromDate,
            accessionNumber: "0001-24-000001");
        var txOnTo = CreateTransaction(stock, owner,
            transactionDate: toDate,
            accessionNumber: "0001-24-000002");
        _repository.AddRange([txOnFrom, txOnTo]);
        await _repository.SaveChanges();

        var result = await _repository.GetByStock(stock, fromDate, toDate).ToListAsync();

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetByStock_DateRange_NoMatchingDates_ReturnsEmpty() {
        var stock = CreateStock();
        var owner = CreateOwner();
        await SeedStockAndOwner(stock, owner);

        var tx = CreateTransaction(stock, owner, transactionDate: new DateOnly(2024, 1, 15));
        _repository.Add(tx);
        await _repository.SaveChanges();

        var from = new DateOnly(2024, 6, 1);
        var to = new DateOnly(2024, 12, 31);
        var result = await _repository.GetByStock(stock, from, to).ToListAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByStock_DateRange_DoesNotReturnOtherStocks() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        var msft = CreateStock("MSFT", "Microsoft Corp.");
        var owner = CreateOwner();
        _dbContext.Set<CommonStock>().AddRange(apple, msft);
        _dbContext.Set<InsiderOwner>().Add(owner);
        await _dbContext.SaveChangesAsync();

        var date = new DateOnly(2024, 5, 15);
        var appleTx = CreateTransaction(apple, owner,
            transactionDate: date, accessionNumber: "0001-24-000001");
        var msftTx = CreateTransaction(msft, owner,
            transactionDate: date, accessionNumber: "0001-24-000002");
        _repository.AddRange([appleTx, msftTx]);
        await _repository.SaveChanges();

        var result = await _repository
            .GetByStock(apple, new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31))
            .ToListAsync();

        result.Should().ContainSingle()
            .Which.Id.Should().Be(appleTx.Id);
    }

    // ── GetByOwner ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetByOwner_OwnerWithTransactions_ReturnsAll() {
        var stock = CreateStock();
        var owner = CreateOwner();
        await SeedStockAndOwner(stock, owner);

        var tx1 = CreateTransaction(stock, owner, accessionNumber: "0001-24-000001");
        var tx2 = CreateTransaction(stock, owner,
            transactionDate: new DateOnly(2024, 7, 1),
            accessionNumber: "0001-24-000002");
        _repository.AddRange([tx1, tx2]);
        await _repository.SaveChanges();

        var result = await _repository.GetByOwner(owner).ToListAsync();

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetByOwner_OwnerWithNoTransactions_ReturnsEmpty() {
        var stock = CreateStock();
        var owner = CreateOwner();
        await SeedStockAndOwner(stock, owner);

        var result = await _repository.GetByOwner(owner).ToListAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByOwner_DoesNotReturnTransactionsForOtherOwners() {
        var stock = CreateStock();
        var owner1 = CreateOwner(cik: "0001111111", name: "Alice");
        var owner2 = CreateOwner(cik: "0002222222", name: "Bob");
        _dbContext.Set<CommonStock>().Add(stock);
        _dbContext.Set<InsiderOwner>().AddRange(owner1, owner2);
        await _dbContext.SaveChangesAsync();

        var tx1 = CreateTransaction(stock, owner1, accessionNumber: "0001-24-000001");
        var tx2 = CreateTransaction(stock, owner2, accessionNumber: "0001-24-000002");
        _repository.AddRange([tx1, tx2]);
        await _repository.SaveChanges();

        var result = await _repository.GetByOwner(owner1).ToListAsync();

        result.Should().ContainSingle()
            .Which.Id.Should().Be(tx1.Id);
    }

    [Fact]
    public async Task GetByOwner_ReturnsQueryable() {
        var stock = CreateStock();
        var owner = CreateOwner();
        await SeedStockAndOwner(stock, owner);

        var result = _repository.GetByOwner(owner);

        result.Should().BeAssignableTo<IQueryable<InsiderTransaction>>();
    }

    // ── GetHistoryByStock ──────────────────────────────────────────────

    [Fact]
    public async Task GetHistoryByStock_ReturnsAllTransactionsForStock() {
        var stock = CreateStock();
        var owner = CreateOwner();
        await SeedStockAndOwner(stock, owner);

        var tx1 = CreateTransaction(stock, owner,
            transactionDate: new DateOnly(2023, 1, 1),
            accessionNumber: "0001-23-000001");
        var tx2 = CreateTransaction(stock, owner,
            transactionDate: new DateOnly(2024, 6, 1),
            accessionNumber: "0001-24-000001");
        _repository.AddRange([tx1, tx2]);
        await _repository.SaveChanges();

        var result = await _repository.GetHistoryByStock(stock).ToListAsync();

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetHistoryByStock_DoesNotReturnOtherStocks() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        var msft = CreateStock("MSFT", "Microsoft Corp.");
        var owner = CreateOwner();
        _dbContext.Set<CommonStock>().AddRange(apple, msft);
        _dbContext.Set<InsiderOwner>().Add(owner);
        await _dbContext.SaveChangesAsync();

        var appleTx = CreateTransaction(apple, owner, accessionNumber: "0001-24-000001");
        var msftTx = CreateTransaction(msft, owner, accessionNumber: "0001-24-000002");
        _repository.AddRange([appleTx, msftTx]);
        await _repository.SaveChanges();

        var result = await _repository.GetHistoryByStock(apple).ToListAsync();

        result.Should().ContainSingle()
            .Which.Id.Should().Be(appleTx.Id);
    }

    // ── GetByAccessionNumber ───────────────────────────────────────────

    [Fact]
    public async Task GetByAccessionNumber_MatchingNumber_ReturnsTransactions() {
        var stock = CreateStock();
        var owner = CreateOwner();
        await SeedStockAndOwner(stock, owner);

        var tx = CreateTransaction(stock, owner, accessionNumber: "0001234567-24-000001");
        _repository.Add(tx);
        await _repository.SaveChanges();

        var result = await _repository
            .GetByAccessionNumber("0001234567-24-000001")
            .ToListAsync();

        result.Should().ContainSingle()
            .Which.Id.Should().Be(tx.Id);
    }

    [Fact]
    public async Task GetByAccessionNumber_MultipleTransactionsSameAccession_ReturnsAll() {
        var stock = CreateStock();
        var owner1 = CreateOwner(cik: "0001111111", name: "Alice");
        var owner2 = CreateOwner(cik: "0002222222", name: "Bob");
        _dbContext.Set<CommonStock>().Add(stock);
        _dbContext.Set<InsiderOwner>().AddRange(owner1, owner2);
        await _dbContext.SaveChangesAsync();

        var accession = "0001234567-24-000099";
        var tx1 = CreateTransaction(stock, owner1, accessionNumber: accession);
        var tx2 = CreateTransaction(stock, owner2, accessionNumber: accession);
        _repository.AddRange([tx1, tx2]);
        await _repository.SaveChanges();

        var result = await _repository.GetByAccessionNumber(accession).ToListAsync();

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetByAccessionNumber_NoMatch_ReturnsEmpty() {
        var stock = CreateStock();
        var owner = CreateOwner();
        await SeedStockAndOwner(stock, owner);

        var tx = CreateTransaction(stock, owner, accessionNumber: "0001234567-24-000001");
        _repository.Add(tx);
        await _repository.SaveChanges();

        var result = await _repository
            .GetByAccessionNumber("9999999999-99-999999")
            .ToListAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByAccessionNumber_ReturnsQueryable() {
        var stock = CreateStock();
        var owner = CreateOwner();
        await SeedStockAndOwner(stock, owner);

        var result = _repository.GetByAccessionNumber("0001234567-24-000001");

        result.Should().BeAssignableTo<IQueryable<InsiderTransaction>>();
    }

}
