using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Mcp.Tools;
using Equibles.InsiderTrading.Repositories;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

[Collection(ParadeDbCollection.Name)]
public class InsiderTradingToolsTests : ParadeDbMcpTestBase {
    public InsiderTradingToolsTests(ParadeDbFixture fixture) : base(fixture) { }

    private InsiderTradingTools Sut() => new(
        new InsiderTransactionRepository(DbContext),
        new InsiderOwnerRepository(DbContext),
        new CommonStockRepository(DbContext),
        ErrorManager,
        NullLogger<InsiderTradingTools>());

    // ── Helpers ────────────────────────────────────────────────────────

    private static CommonStock CreateStock(string ticker = "AAPL", string name = "Apple Inc.") {
        return new CommonStock {
            Ticker = ticker, Name = name,
            Cik = Random.Shared.NextInt64(1_000_000_000L, 9_999_999_999L).ToString(),
        };
    }

    private static InsiderOwner CreateOwner(
        string cik = "0001234567", string name = "John Doe",
        string city = "New York", string state = "NY",
        bool isDirector = true, bool isOfficer = false,
        string officerTitle = null, bool isTenPercentOwner = false
    ) {
        return new InsiderOwner {
            OwnerCik = cik, Name = name,
            City = city, StateOrCountry = state,
            IsDirector = isDirector, IsOfficer = isOfficer,
            OfficerTitle = officerTitle, IsTenPercentOwner = isTenPercentOwner,
        };
    }

    private static InsiderTransaction CreateTransaction(
        CommonStock stock, InsiderOwner owner,
        DateOnly? transactionDate = null, DateOnly? filingDate = null,
        TransactionCode code = TransactionCode.Purchase,
        long shares = 1000, decimal pricePerShare = 150.00m,
        AcquiredDisposed acquiredDisposed = AcquiredDisposed.Acquired,
        long sharesOwnedAfter = 5000,
        string securityTitle = "Common Stock",
        string accessionNumber = "0001234567-24-000001"
    ) {
        return new InsiderTransaction {
            CommonStockId = stock.Id, CommonStock = stock,
            InsiderOwnerId = owner.Id, InsiderOwner = owner,
            TransactionDate = transactionDate ?? new DateOnly(2024, 6, 14),
            FilingDate = filingDate ?? new DateOnly(2024, 6, 15),
            TransactionCode = code,
            Shares = shares, PricePerShare = pricePerShare,
            AcquiredDisposed = acquiredDisposed,
            SharesOwnedAfter = sharesOwnedAfter,
            OwnershipNature = OwnershipNature.Direct,
            SecurityTitle = securityTitle,
            AccessionNumber = accessionNumber,
        };
    }

    private async Task SeedStock(CommonStock stock) {
        DbContext.Set<CommonStock>().Add(stock);
        await DbContext.SaveChangesAsync();
    }

    private async Task SeedOwner(InsiderOwner owner) {
        DbContext.Set<InsiderOwner>().Add(owner);
        await DbContext.SaveChangesAsync();
    }

    private async Task SeedTransaction(InsiderTransaction transaction) {
        DbContext.Set<InsiderTransaction>().Add(transaction);
        await DbContext.SaveChangesAsync();
    }

    // ══════════════════════════════════════════════════════════════════
    // GetInsiderTransactions
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetInsiderTransactions_StockNotFound_ReturnsNotFoundMessage() {
        var result = await Sut().GetInsiderTransactions("ZZZZ");

        result.Should().Contain("Stock 'ZZZZ' not found.");
    }

    [Fact]
    public async Task GetInsiderTransactions_StockWithNoTransactions_ReturnsNoTransactionsMessage() {
        await SeedStock(CreateStock("AAPL", "Apple Inc."));

        var result = await Sut().GetInsiderTransactions("AAPL");

        result.Should().Contain("No insider transactions found for AAPL.");
    }

    [Fact]
    public async Task GetInsiderTransactions_StockWithTransactions_ReturnsFormattedTable() {
        var stock = CreateStock("AAPL", "Apple Inc.");
        var owner = CreateOwner(name: "Tim Cook", isDirector: false, isOfficer: true, officerTitle: "CEO");
        await SeedStock(stock);
        await SeedOwner(owner);
        await SeedTransaction(CreateTransaction(stock, owner,
            transactionDate: new DateOnly(2024, 3, 15),
            code: TransactionCode.Sale, shares: 50000, pricePerShare: 175.50m,
            acquiredDisposed: AcquiredDisposed.Disposed,
            sharesOwnedAfter: 200000,
            accessionNumber: "0001-24-000001"));

        var result = await Sut().GetInsiderTransactions("AAPL");

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

        DbContext.Set<InsiderTransaction>().AddRange(
            CreateTransaction(stock, owner, transactionDate: new DateOnly(2024, 1, 10),
                accessionNumber: "0001-24-000001"),
            CreateTransaction(stock, owner, transactionDate: new DateOnly(2024, 6, 20),
                accessionNumber: "0001-24-000002"),
            CreateTransaction(stock, owner, transactionDate: new DateOnly(2024, 3, 15),
                accessionNumber: "0001-24-000003"));
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetInsiderTransactions("MSFT");

        var dateNew = result.IndexOf("2024-06-20", StringComparison.Ordinal);
        var dateMid = result.IndexOf("2024-03-15", StringComparison.Ordinal);
        var dateOld = result.IndexOf("2024-01-10", StringComparison.Ordinal);

        dateNew.Should().BeLessThan(dateMid);
        dateMid.Should().BeLessThan(dateOld);
    }

    [Fact]
    public async Task GetInsiderTransactions_RespectsMaxResults() {
        var stock = CreateStock("AAPL", "Apple Inc.");
        var owner = CreateOwner(name: "Insider A");
        await SeedStock(stock);
        await SeedOwner(owner);

        for (var i = 0; i < 5; i++) {
            DbContext.Set<InsiderTransaction>().Add(CreateTransaction(stock, owner,
                transactionDate: new DateOnly(2024, 1 + i, 1),
                accessionNumber: $"0001-24-{i:D6}"));
        }
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetInsiderTransactions("AAPL", maxResults: 3);

        result.Should().Contain("Showing 3 most recent transactions");
    }

    [Fact]
    public async Task GetInsiderTransactions_AcquiredTransaction_ShowsBuy() {
        var stock = CreateStock("AAPL", "Apple Inc.");
        var owner = CreateOwner(name: "Buyer");
        await SeedStock(stock);
        await SeedOwner(owner);
        await SeedTransaction(CreateTransaction(stock, owner,
            code: TransactionCode.Purchase, acquiredDisposed: AcquiredDisposed.Acquired));

        var result = await Sut().GetInsiderTransactions("AAPL");

        result.Should().Contain("Buy");
    }

    [Fact]
    public async Task GetInsiderTransactions_AwardTransaction_ShowsAward() {
        var stock = CreateStock("AAPL", "Apple Inc.");
        var owner = CreateOwner(name: "Awardee");
        await SeedStock(stock);
        await SeedOwner(owner);
        await SeedTransaction(CreateTransaction(stock, owner,
            code: TransactionCode.Award, acquiredDisposed: AcquiredDisposed.Acquired));

        var result = await Sut().GetInsiderTransactions("AAPL");

        result.Should().Contain("Award");
    }

    [Fact]
    public async Task GetInsiderTransactions_GiftTransaction_ShowsGift() {
        var stock = CreateStock("AAPL", "Apple Inc.");
        var owner = CreateOwner(name: "Gifter");
        await SeedStock(stock);
        await SeedOwner(owner);
        await SeedTransaction(CreateTransaction(stock, owner,
            code: TransactionCode.Gift, acquiredDisposed: AcquiredDisposed.Disposed));

        var result = await Sut().GetInsiderTransactions("AAPL");

        result.Should().Contain("Gift");
    }

    [Fact]
    public async Task GetInsiderTransactions_ExerciseTransaction_ShowsExercise() {
        var stock = CreateStock("AAPL", "Apple Inc.");
        var owner = CreateOwner(name: "Exerciser");
        await SeedStock(stock);
        await SeedOwner(owner);
        await SeedTransaction(CreateTransaction(stock, owner,
            code: TransactionCode.Exercise, acquiredDisposed: AcquiredDisposed.Acquired));

        var result = await Sut().GetInsiderTransactions("AAPL");

        result.Should().Contain("Exercise");
    }

    [Fact]
    public async Task GetInsiderTransactions_OwnerWithMultipleRoles_ShowsAllRoles() {
        var stock = CreateStock("AAPL", "Apple Inc.");
        var owner = CreateOwner(name: "Multi Role",
            isDirector: true, isOfficer: true, officerTitle: "CFO", isTenPercentOwner: true);
        await SeedStock(stock);
        await SeedOwner(owner);
        await SeedTransaction(CreateTransaction(stock, owner));

        var result = await Sut().GetInsiderTransactions("AAPL");

        result.Should().Contain("Director");
        result.Should().Contain("CFO");
        result.Should().Contain("10% Owner");
    }

    [Fact]
    public async Task GetInsiderTransactions_OwnerWithNoRoles_ShowsInsider() {
        var stock = CreateStock("AAPL", "Apple Inc.");
        var owner = CreateOwner(name: "Plain Insider",
            isDirector: false, isOfficer: false, isTenPercentOwner: false);
        await SeedStock(stock);
        await SeedOwner(owner);
        await SeedTransaction(CreateTransaction(stock, owner));

        var result = await Sut().GetInsiderTransactions("AAPL");

        result.Should().Contain("Insider");
    }

    [Fact]
    public async Task GetInsiderTransactions_ContainsTableHeader() {
        var stock = CreateStock("AAPL", "Apple Inc.");
        var owner = CreateOwner(name: "Header Test");
        await SeedStock(stock);
        await SeedOwner(owner);
        await SeedTransaction(CreateTransaction(stock, owner));

        var result = await Sut().GetInsiderTransactions("AAPL");

        result.Should().Contain("| Date | Insider | Role | Type | Shares | Price | Value | Owned After |");
    }

    // ══════════════════════════════════════════════════════════════════
    // GetInsiderOwnership
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetInsiderOwnership_StockNotFound_ReturnsNotFoundMessage() {
        var result = await Sut().GetInsiderOwnership("ZZZZ");

        result.Should().Contain("Stock 'ZZZZ' not found.");
    }

    [Fact]
    public async Task GetInsiderOwnership_ReturnsOwnershipSummary() {
        var stock = CreateStock("AAPL", "Apple Inc.");
        var owner = CreateOwner(name: "Tim Cook", isDirector: false, isOfficer: true, officerTitle: "CEO");
        await SeedStock(stock);
        await SeedOwner(owner);
        await SeedTransaction(CreateTransaction(stock, owner, accessionNumber: "0001-24-000001"));

        var result = await Sut().GetInsiderOwnership("AAPL");

        result.Should().Contain("Insider ownership summary for Apple Inc. (AAPL):");
        result.Should().Contain("Tim Cook");
        result.Should().Contain("CEO");
    }

    [Fact]
    public async Task GetInsiderOwnership_NoData_ReturnsNoDataMessage() {
        await SeedStock(CreateStock("AAPL", "Apple Inc."));

        var result = await Sut().GetInsiderOwnership("AAPL");

        result.Should().Contain("No insider ownership data found for AAPL.");
    }

    // ══════════════════════════════════════════════════════════════════
    // SearchInsiders
    // ══════════════════════════════════════════════════════════════════
    //
    // The repository uses EF.Functions.ILike — pre-Postgres, the InMemory provider
    // threw on ILike and the tests asserted on the catch-block error message.
    // Real Postgres handles ILike natively, so we now assert on the real match.

    [Fact]
    public async Task SearchInsiders_MatchesByPartialName_ReturnsResults() {
        DbContext.Set<InsiderOwner>().AddRange(
            CreateOwner(cik: "0000111", name: "Warren Buffett"),
            CreateOwner(cik: "0000222", name: "Charlie Munger"),
            CreateOwner(cik: "0000333", name: "Bill Gates"));
        await DbContext.SaveChangesAsync();

        var result = await Sut().SearchInsiders("buffett");

        result.Should().Contain("Warren Buffett");
        result.Should().NotContain("Charlie Munger");
        result.Should().NotContain("Bill Gates");
    }

    [Fact]
    public async Task SearchInsiders_IsCaseInsensitive() {
        DbContext.Set<InsiderOwner>().Add(CreateOwner(name: "Warren Buffett"));
        await DbContext.SaveChangesAsync();

        var result = await Sut().SearchInsiders("WARREN");

        result.Should().Contain("Warren Buffett");
    }

    [Fact]
    public async Task SearchInsiders_NoMatches_ReturnsNotFoundMessage() {
        DbContext.Set<InsiderOwner>().Add(CreateOwner(name: "Warren Buffett"));
        await DbContext.SaveChangesAsync();

        var result = await Sut().SearchInsiders("Nonexistent");

        result.Should().Contain("No insiders found matching 'Nonexistent'");
    }
}
