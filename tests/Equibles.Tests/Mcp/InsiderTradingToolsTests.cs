using System.Globalization;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data;
using Equibles.Errors.Repositories;
using Equibles.InsiderTrading.Data;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Mcp.Tools;
using Equibles.InsiderTrading.Repositories;
using Equibles.Tests.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.Tests.Mcp;

public class InsiderTradingToolsTests : IDisposable {
    private readonly EquiblesDbContext _dbContext;
    private readonly InsiderTransactionRepository _transactionRepository;
    private readonly InsiderOwnerRepository _ownerRepository;
    private readonly CommonStockRepository _commonStockRepository;
    private readonly ErrorManager _errorManager;
    private readonly ILogger<InsiderTradingTools> _logger;
    private readonly InsiderTradingTools _sut;
    private readonly CultureInfo _previousCulture;

    public InsiderTradingToolsTests() {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new InsiderTradingModuleConfiguration(),
            new ErrorsModuleConfiguration());

        _transactionRepository = new InsiderTransactionRepository(_dbContext);
        _ownerRepository = new InsiderOwnerRepository(_dbContext);
        _commonStockRepository = new CommonStockRepository(_dbContext);
        _errorManager = new ErrorManager(new ErrorRepository(_dbContext));
        _logger = Substitute.For<ILogger<InsiderTradingTools>>();

        _sut = new InsiderTradingTools(
            _transactionRepository,
            _ownerRepository,
            _commonStockRepository,
            _errorManager,
            _logger);

        // Fix culture for deterministic formatting (N0, N2 format specifiers)
        _previousCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    public void Dispose() {
        CultureInfo.CurrentCulture = _previousCulture;
        _dbContext.Dispose();
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static CommonStock CreateStock(string ticker = "AAPL", string name = "Apple Inc.") {
        return new CommonStock {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = name,
        };
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

    private static InsiderTransaction CreateTransaction(
        CommonStock stock,
        InsiderOwner owner,
        DateOnly? transactionDate = null,
        DateOnly? filingDate = null,
        TransactionCode code = TransactionCode.Purchase,
        long shares = 1000,
        decimal pricePerShare = 150.00m,
        AcquiredDisposed acquiredDisposed = AcquiredDisposed.Acquired,
        long sharesOwnedAfter = 5000,
        string securityTitle = "Common Stock",
        string accessionNumber = "0001234567-24-000001") {
        return new InsiderTransaction {
            Id = Guid.NewGuid(),
            CommonStockId = stock.Id,
            CommonStock = stock,
            InsiderOwnerId = owner.Id,
            InsiderOwner = owner,
            TransactionDate = transactionDate ?? new DateOnly(2024, 6, 14),
            FilingDate = filingDate ?? new DateOnly(2024, 6, 15),
            TransactionCode = code,
            Shares = shares,
            PricePerShare = pricePerShare,
            AcquiredDisposed = acquiredDisposed,
            SharesOwnedAfter = sharesOwnedAfter,
            OwnershipNature = OwnershipNature.Direct,
            SecurityTitle = securityTitle,
            AccessionNumber = accessionNumber,
        };
    }

    private async Task SeedStock(CommonStock stock) {
        _dbContext.Set<CommonStock>().Add(stock);
        await _dbContext.SaveChangesAsync();
    }

    private async Task SeedOwner(InsiderOwner owner) {
        _dbContext.Set<InsiderOwner>().Add(owner);
        await _dbContext.SaveChangesAsync();
    }

    private async Task SeedTransaction(InsiderTransaction transaction) {
        _transactionRepository.Add(transaction);
        await _transactionRepository.SaveChanges();
    }

    // ══════════════════════════════════════════════════════════════════
    // GetInsiderTransactions
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetInsiderTransactions_StockNotFound_ReturnsNotFoundMessage() {
        var result = await _sut.GetInsiderTransactions("ZZZZ");

        result.Should().Contain("Stock 'ZZZZ' not found.");
    }

    [Fact]
    public async Task GetInsiderTransactions_StockWithNoTransactions_ReturnsNoTransactionsMessage() {
        var stock = CreateStock("AAPL", "Apple Inc.");
        await SeedStock(stock);

        var result = await _sut.GetInsiderTransactions("AAPL");

        result.Should().Contain("No insider transactions found for AAPL.");
    }

    [Fact]
    public async Task GetInsiderTransactions_StockWithTransactions_ReturnsFormattedTable() {
        var stock = CreateStock("AAPL", "Apple Inc.");
        var owner = CreateOwner(name: "Tim Cook", isDirector: false, isOfficer: true, officerTitle: "CEO");
        await SeedStock(stock);
        await SeedOwner(owner);

        var tx = CreateTransaction(stock, owner,
            transactionDate: new DateOnly(2024, 3, 15),
            code: TransactionCode.Sale,
            shares: 50000,
            pricePerShare: 175.50m,
            acquiredDisposed: AcquiredDisposed.Disposed,
            sharesOwnedAfter: 200000,
            accessionNumber: "0001-24-000001");
        await SeedTransaction(tx);

        var result = await _sut.GetInsiderTransactions("AAPL");

        result.Should().Contain("Apple Inc. (AAPL)");
        result.Should().Contain("Tim Cook");
        result.Should().Contain("CEO");
        result.Should().Contain("Sell");
        result.Should().Contain("2024-03-15");
        result.Should().Contain("50,000");
        result.Should().Contain("$175.50");
        result.Should().Contain("200,000");
    }

    [Fact]
    public async Task GetInsiderTransactions_MultipleTransactions_OrderedByDateDescending() {
        var stock = CreateStock("MSFT", "Microsoft Corp.");
        var owner = CreateOwner(cik: "0001111111", name: "Satya Nadella", isDirector: true);
        await SeedStock(stock);
        await SeedOwner(owner);

        var txOld = CreateTransaction(stock, owner,
            transactionDate: new DateOnly(2024, 1, 10),
            accessionNumber: "0001-24-000001");
        var txNew = CreateTransaction(stock, owner,
            transactionDate: new DateOnly(2024, 6, 20),
            accessionNumber: "0001-24-000002");
        var txMid = CreateTransaction(stock, owner,
            transactionDate: new DateOnly(2024, 3, 15),
            accessionNumber: "0001-24-000003");
        _transactionRepository.AddRange([txOld, txNew, txMid]);
        await _transactionRepository.SaveChanges();

        var result = await _sut.GetInsiderTransactions("MSFT");

        var dateNew = result.IndexOf("2024-06-20", StringComparison.Ordinal);
        var dateMid = result.IndexOf("2024-03-15", StringComparison.Ordinal);
        var dateOld = result.IndexOf("2024-01-10", StringComparison.Ordinal);

        dateNew.Should().BeLessThan(dateMid, "newest transaction should appear first");
        dateMid.Should().BeLessThan(dateOld, "middle transaction should appear before oldest");
    }

    [Fact]
    public async Task GetInsiderTransactions_RespectsMaxResults() {
        var stock = CreateStock("AAPL", "Apple Inc.");
        var owner = CreateOwner(name: "Insider A");
        await SeedStock(stock);
        await SeedOwner(owner);

        for (var i = 0; i < 5; i++) {
            var tx = CreateTransaction(stock, owner,
                transactionDate: new DateOnly(2024, 1 + i, 1),
                accessionNumber: $"0001-24-{i:D6}");
            _transactionRepository.Add(tx);
        }

        await _transactionRepository.SaveChanges();

        var result = await _sut.GetInsiderTransactions("AAPL", maxResults: 3);

        result.Should().Contain("Showing 3 most recent transactions");
    }

    [Fact]
    public async Task GetInsiderTransactions_AcquiredTransaction_ShowsBuy() {
        var stock = CreateStock("AAPL", "Apple Inc.");
        var owner = CreateOwner(name: "Buyer");
        await SeedStock(stock);
        await SeedOwner(owner);

        var tx = CreateTransaction(stock, owner,
            code: TransactionCode.Purchase,
            acquiredDisposed: AcquiredDisposed.Acquired);
        await SeedTransaction(tx);

        var result = await _sut.GetInsiderTransactions("AAPL");

        result.Should().Contain("Buy");
    }

    [Fact]
    public async Task GetInsiderTransactions_AwardTransaction_ShowsAward() {
        var stock = CreateStock("AAPL", "Apple Inc.");
        var owner = CreateOwner(name: "Awardee");
        await SeedStock(stock);
        await SeedOwner(owner);

        var tx = CreateTransaction(stock, owner,
            code: TransactionCode.Award,
            acquiredDisposed: AcquiredDisposed.Acquired);
        await SeedTransaction(tx);

        var result = await _sut.GetInsiderTransactions("AAPL");

        result.Should().Contain("Award");
    }

    [Fact]
    public async Task GetInsiderTransactions_GiftTransaction_ShowsGift() {
        var stock = CreateStock("AAPL", "Apple Inc.");
        var owner = CreateOwner(name: "Gifter");
        await SeedStock(stock);
        await SeedOwner(owner);

        var tx = CreateTransaction(stock, owner,
            code: TransactionCode.Gift,
            acquiredDisposed: AcquiredDisposed.Disposed);
        await SeedTransaction(tx);

        var result = await _sut.GetInsiderTransactions("AAPL");

        result.Should().Contain("Gift");
    }

    [Fact]
    public async Task GetInsiderTransactions_ExerciseTransaction_ShowsExercise() {
        var stock = CreateStock("AAPL", "Apple Inc.");
        var owner = CreateOwner(name: "Exerciser");
        await SeedStock(stock);
        await SeedOwner(owner);

        var tx = CreateTransaction(stock, owner,
            code: TransactionCode.Exercise,
            acquiredDisposed: AcquiredDisposed.Acquired);
        await SeedTransaction(tx);

        var result = await _sut.GetInsiderTransactions("AAPL");

        result.Should().Contain("Exercise");
    }

    [Fact]
    public async Task GetInsiderTransactions_OwnerWithMultipleRoles_ShowsAllRoles() {
        var stock = CreateStock("AAPL", "Apple Inc.");
        var owner = CreateOwner(
            name: "Multi Role",
            isDirector: true,
            isOfficer: true,
            officerTitle: "CFO",
            isTenPercentOwner: true);
        await SeedStock(stock);
        await SeedOwner(owner);

        var tx = CreateTransaction(stock, owner);
        await SeedTransaction(tx);

        var result = await _sut.GetInsiderTransactions("AAPL");

        result.Should().Contain("Director");
        result.Should().Contain("CFO");
        result.Should().Contain("10% Owner");
    }

    [Fact]
    public async Task GetInsiderTransactions_OwnerWithNoRoles_ShowsInsider() {
        var stock = CreateStock("AAPL", "Apple Inc.");
        var owner = CreateOwner(
            name: "Plain Insider",
            isDirector: false,
            isOfficer: false,
            isTenPercentOwner: false);
        await SeedStock(stock);
        await SeedOwner(owner);

        var tx = CreateTransaction(stock, owner);
        await SeedTransaction(tx);

        var result = await _sut.GetInsiderTransactions("AAPL");

        result.Should().Contain("Insider");
    }

    [Fact]
    public async Task GetInsiderTransactions_ContainsTableHeader() {
        var stock = CreateStock("AAPL", "Apple Inc.");
        var owner = CreateOwner(name: "Header Test");
        await SeedStock(stock);
        await SeedOwner(owner);

        var tx = CreateTransaction(stock, owner);
        await SeedTransaction(tx);

        var result = await _sut.GetInsiderTransactions("AAPL");

        result.Should().Contain("| Date | Insider | Role | Type | Shares | Price | Value | Owned After |");
    }

    // ══════════════════════════════════════════════════════════════════
    // GetInsiderOwnership
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetInsiderOwnership_StockNotFound_ReturnsNotFoundMessage() {
        var result = await _sut.GetInsiderOwnership("ZZZZ");

        result.Should().Contain("Stock 'ZZZZ' not found.");
    }

    [Fact]
    public async Task GetInsiderOwnership_ReturnsOwnershipSummary() {
        var stock = CreateStock("AAPL", "Apple Inc.");
        var owner = CreateOwner(name: "Tim Cook", isDirector: false, isOfficer: true, officerTitle: "CEO");
        await SeedStock(stock);
        await SeedOwner(owner);

        var tx = CreateTransaction(stock, owner, accessionNumber: "0001-24-000001");
        await SeedTransaction(tx);

        var result = await _sut.GetInsiderOwnership("AAPL");

        result.Should().Contain("Insider ownership summary for Apple Inc. (AAPL):");
        result.Should().Contain("Tim Cook");
        result.Should().Contain("CEO");
    }

    [Fact]
    public async Task GetInsiderOwnership_NoData_ReturnsNoDataMessage() {
        var stock = CreateStock("AAPL", "Apple Inc.");
        await SeedStock(stock);

        var result = await _sut.GetInsiderOwnership("AAPL");

        result.Should().Contain("No insider ownership data found for AAPL.");
    }

    // ══════════════════════════════════════════════════════════════════
    // SearchInsiders
    // ══════════════════════════════════════════════════════════════════
    //
    // Note: InsiderOwnerRepository.Search uses EF.Functions.ILike which
    // is not supported by the in-memory provider. The McpToolExecutor
    // catches the resulting exception and returns a generic error
    // message. We verify that error-handling path here.
    //
    // For full integration coverage, use a PostgreSQL test database.
    // The repository method is not virtual, so it cannot be mocked
    // with NSubstitute either.

    [Fact]
    public async Task SearchInsiders_ILikeNotSupportedInMemory_ReturnsErrorMessage() {
        var owner = CreateOwner(name: "Warren Buffett");
        await SeedOwner(owner);

        var result = await _sut.SearchInsiders("Warren");

        result.Should().Contain("An error occurred while executing SearchInsiders");
    }

    [Fact]
    public async Task SearchInsiders_ErrorIsReportedToErrorManager() {
        var owner = CreateOwner(name: "Test Person");
        await SeedOwner(owner);

        await _sut.SearchInsiders("Test");

        var errors = _dbContext.Set<Equibles.Errors.Data.Models.Error>().ToList();
        errors.Should().ContainSingle();
        errors[0].Context.Should().Be("SearchInsiders");
    }
}
