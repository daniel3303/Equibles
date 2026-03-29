using System.Globalization;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Repositories;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories;
using Equibles.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.Tests.Mcp;

public class InstitutionalHoldingsToolsTests : IDisposable {
    private readonly EquiblesDbContext _dbContext;
    private readonly CommonStockRepository _commonStockRepository;
    private readonly InstitutionalHoldingRepository _holdingRepository;
    private readonly ErrorManager _errorManager;
    private readonly ILogger<InstitutionalHoldingsTools> _logger;
    private readonly InstitutionalHoldingsTools _sut;
    private readonly CultureInfo _previousCulture;

    public InstitutionalHoldingsToolsTests() {
        // Force invariant culture so number formatting is deterministic (e.g. "10,000" not "10 000")
        _previousCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new HoldingsModuleConfiguration());

        _commonStockRepository = new CommonStockRepository(_dbContext);
        _holdingRepository = new InstitutionalHoldingRepository(_dbContext);

        _errorManager = Substitute.For<ErrorManager>(
            Substitute.For<ErrorRepository>(
                Substitute.For<EquiblesDbContext>(
                    new DbContextOptions<EquiblesDbContext>(),
                    Array.Empty<IModuleConfiguration>())));
        _logger = Substitute.For<ILogger<InstitutionalHoldingsTools>>();

        // Default SUT uses a real InstitutionalHolderRepository — fine for
        // GetTopHolders / GetOwnershipHistory which don't call Search.
        var holderRepository = new InstitutionalHolderRepository(_dbContext);
        _sut = new InstitutionalHoldingsTools(
            _holdingRepository,
            holderRepository,
            _commonStockRepository,
            _errorManager,
            _logger);
    }

    public void Dispose() {
        CultureInfo.CurrentCulture = _previousCulture;
        _dbContext.Dispose();
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static CommonStock CreateStock(string ticker = "AAPL", string name = "Apple Inc") {
        return new CommonStock {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = name,
            Cik = Guid.NewGuid().ToString()[..10],
        };
    }

    private static InstitutionalHolder CreateHolder(
        string cik = "0001067983",
        string name = "Berkshire Hathaway Inc",
        string city = "Omaha",
        string stateOrCountry = "NE") {
        return new InstitutionalHolder {
            Id = Guid.NewGuid(),
            Cik = cik,
            Name = name,
            City = city,
            StateOrCountry = stateOrCountry,
        };
    }

    private static InstitutionalHolding CreateHolding(
        CommonStock stock,
        InstitutionalHolder holder,
        DateOnly reportDate,
        long shares = 1000,
        long value = 50_000,
        string accessionNumber = null) {
        return new InstitutionalHolding {
            Id = Guid.NewGuid(),
            CommonStockId = stock.Id,
            CommonStock = stock,
            InstitutionalHolderId = holder.Id,
            InstitutionalHolder = holder,
            ReportDate = reportDate,
            FilingDate = reportDate.AddDays(45),
            Shares = shares,
            Value = value,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = accessionNumber ?? $"0000000000-24-{Guid.NewGuid().ToString()[..6]}",
            TitleOfClass = "COM",
            Cusip = "037833100",
        };
    }

    private async Task SeedStockAndHolder(CommonStock stock, InstitutionalHolder holder) {
        _dbContext.Set<CommonStock>().Add(stock);
        _dbContext.Set<InstitutionalHolder>().Add(holder);
        await _dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// Creates a tools instance that uses ForPartsOf on InstitutionalHolderRepository
    /// with GetAll() returning an ILikeSafeQueryable so that the non-virtual Search method
    /// (which chains EF.Functions.ILike on GetAll()) can run against in-memory data.
    /// </summary>
    private InstitutionalHoldingsTools CreateToolsWithSearchableHolders(params InstitutionalHolder[] holders) {
        var mockHolderRepo = Substitute.ForPartsOf<InstitutionalHolderRepository>(_dbContext);
        mockHolderRepo.GetAll()
            .Returns(new ILikeSafeQueryable<InstitutionalHolder>(holders));

        return new InstitutionalHoldingsTools(
            _holdingRepository,
            mockHolderRepo,
            _commonStockRepository,
            _errorManager,
            _logger);
    }

    // ── GetTopHolders ───────────────────────────────────────────────────

    [Fact]
    public async Task GetTopHolders_StockFoundWithHolders_ReturnsFormattedTable() {
        var stock = CreateStock("AAPL", "Apple Inc");
        var berkshire = CreateHolder("0001067983", "Berkshire Hathaway");
        var blackrock = CreateHolder("0001166559", "BlackRock Inc");
        _dbContext.Set<CommonStock>().Add(stock);
        _dbContext.Set<InstitutionalHolder>().AddRange(berkshire, blackrock);
        await _dbContext.SaveChangesAsync();

        var reportDate = new DateOnly(2024, 3, 31);
        _dbContext.Set<InstitutionalHolding>().AddRange(
            CreateHolding(stock, berkshire, reportDate, shares: 10_000, value: 1_500_000,
                accessionNumber: "0000000000-24-000001"),
            CreateHolding(stock, blackrock, reportDate, shares: 5_000, value: 750_000,
                accessionNumber: "0000000000-24-000002"));
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetTopHolders("AAPL");

        result.Should().Contain("Apple Inc");
        result.Should().Contain("AAPL");
        result.Should().Contain("2024-03-31");
        result.Should().Contain("Berkshire Hathaway");
        result.Should().Contain("BlackRock Inc");
        result.Should().Contain("10,000");
        result.Should().Contain("5,000");
        result.Should().Contain("2 of 2 institutions");
    }

    [Fact]
    public async Task GetTopHolders_StockNotFound_ReturnsNotFoundMessage() {
        var result = await _sut.GetTopHolders("ZZZZ");

        result.Should().Be("Stock 'ZZZZ' not found.");
    }

    [Fact]
    public async Task GetTopHolders_StockFoundNoHoldings_ReturnsNoDataMessage() {
        var stock = CreateStock("AAPL", "Apple Inc");
        _dbContext.Set<CommonStock>().Add(stock);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetTopHolders("AAPL");

        result.Should().Contain("No institutional holdings data available for AAPL");
    }

    [Fact]
    public async Task GetTopHolders_WithSpecificReportDate_FiltersToThatDate() {
        var stock = CreateStock("AAPL", "Apple Inc");
        var holder = CreateHolder();
        await SeedStockAndHolder(stock, holder);

        var q1 = new DateOnly(2024, 3, 31);
        var q2 = new DateOnly(2024, 6, 30);
        _dbContext.Set<InstitutionalHolding>().AddRange(
            CreateHolding(stock, holder, q1, shares: 1_000, accessionNumber: "0000000000-24-000001"),
            CreateHolding(stock, holder, q2, shares: 2_000, accessionNumber: "0000000000-24-000002"));
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetTopHolders("AAPL", reportDate: "2024-03-31");

        result.Should().Contain("2024-03-31");
        result.Should().Contain("1,000");
        result.Should().NotContain("2,000");
    }

    [Fact]
    public async Task GetTopHolders_DefaultsToLatestReportDate() {
        var stock = CreateStock("AAPL", "Apple Inc");
        var holder = CreateHolder();
        await SeedStockAndHolder(stock, holder);

        var q1 = new DateOnly(2024, 3, 31);
        var q2 = new DateOnly(2024, 6, 30);
        _dbContext.Set<InstitutionalHolding>().AddRange(
            CreateHolding(stock, holder, q1, shares: 1_000, accessionNumber: "0000000000-24-000001"),
            CreateHolding(stock, holder, q2, shares: 2_000, accessionNumber: "0000000000-24-000002"));
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetTopHolders("AAPL");

        result.Should().Contain("2024-06-30");
        result.Should().Contain("2,000");
    }

    [Fact]
    public async Task GetTopHolders_MaxResultsLimitsOutput() {
        var stock = CreateStock("AAPL", "Apple Inc");
        var holders = Enumerable.Range(1, 5).Select(i =>
            CreateHolder($"000{i:D7}", $"Fund {i}")).ToList();
        _dbContext.Set<CommonStock>().Add(stock);
        _dbContext.Set<InstitutionalHolder>().AddRange(holders);
        await _dbContext.SaveChangesAsync();

        var reportDate = new DateOnly(2024, 3, 31);
        var holdings = holders.Select((h, i) =>
            CreateHolding(stock, h, reportDate, shares: (5 - i) * 1000,
                accessionNumber: $"0000000000-24-{i + 1:D6}")).ToList();
        _dbContext.Set<InstitutionalHolding>().AddRange(holdings);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetTopHolders("AAPL", maxResults: 2);

        result.Should().Contain("2 of 5 institutions");
        result.Should().Contain("Fund 1");
        result.Should().Contain("Fund 2");
        result.Should().NotContain("Fund 5");
    }

    [Fact]
    public async Task GetTopHolders_CalculatesPercentageOfTotal() {
        var stock = CreateStock("AAPL", "Apple Inc");
        var holder1 = CreateHolder("0001", "Fund A");
        var holder2 = CreateHolder("0002", "Fund B");
        _dbContext.Set<CommonStock>().Add(stock);
        _dbContext.Set<InstitutionalHolder>().AddRange(holder1, holder2);
        await _dbContext.SaveChangesAsync();

        var reportDate = new DateOnly(2024, 3, 31);
        _dbContext.Set<InstitutionalHolding>().AddRange(
            CreateHolding(stock, holder1, reportDate, shares: 75_000,
                accessionNumber: "0000000000-24-000001"),
            CreateHolding(stock, holder2, reportDate, shares: 25_000,
                accessionNumber: "0000000000-24-000002"));
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetTopHolders("AAPL");

        result.Should().Contain("75.00%");
        result.Should().Contain("25.00%");
    }

    // ── GetOwnershipHistory ─────────────────────────────────────────────

    [Fact]
    public async Task GetOwnershipHistory_StockWithMultipleReportDates_ReturnsChronologicalTable() {
        var stock = CreateStock("AAPL", "Apple Inc");
        var holder = CreateHolder();
        await SeedStockAndHolder(stock, holder);

        var q1 = new DateOnly(2024, 3, 31);
        var q2 = new DateOnly(2024, 6, 30);
        var q3 = new DateOnly(2024, 9, 30);
        _dbContext.Set<InstitutionalHolding>().AddRange(
            CreateHolding(stock, holder, q1, shares: 10_000, value: 1_000_000,
                accessionNumber: "0000000000-24-000001"),
            CreateHolding(stock, holder, q2, shares: 12_000, value: 1_200_000,
                accessionNumber: "0000000000-24-000002"),
            CreateHolding(stock, holder, q3, shares: 15_000, value: 1_500_000,
                accessionNumber: "0000000000-24-000003"));
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetOwnershipHistory("AAPL");

        result.Should().Contain("Institutional ownership history for Apple Inc (AAPL)");
        result.Should().Contain("2024-03-31");
        result.Should().Contain("2024-06-30");
        result.Should().Contain("2024-09-30");
    }

    [Fact]
    public async Task GetOwnershipHistory_StockNotFound_ReturnsNotFoundMessage() {
        var result = await _sut.GetOwnershipHistory("ZZZZ");

        result.Should().Be("Stock 'ZZZZ' not found.");
    }

    [Fact]
    public async Task GetOwnershipHistory_StockFoundNoHistory_ReturnsNoDataMessage() {
        var stock = CreateStock("AAPL", "Apple Inc");
        _dbContext.Set<CommonStock>().Add(stock);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetOwnershipHistory("AAPL");

        result.Should().Contain("No institutional holdings history available for AAPL");
    }

    [Fact]
    public async Task GetOwnershipHistory_ShowsChangePercentageBetweenPeriods() {
        var stock = CreateStock("AAPL", "Apple Inc");
        var holder = CreateHolder();
        await SeedStockAndHolder(stock, holder);

        var q1 = new DateOnly(2024, 3, 31);
        var q2 = new DateOnly(2024, 6, 30);
        _dbContext.Set<InstitutionalHolding>().AddRange(
            CreateHolding(stock, holder, q1, shares: 10_000, accessionNumber: "0000000000-24-000001"),
            CreateHolding(stock, holder, q2, shares: 12_000, accessionNumber: "0000000000-24-000002"));
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetOwnershipHistory("AAPL");

        // First period has em-dash (no previous), second has +20.0%
        result.Should().Contain("\u2014");
        result.Should().Contain("+20.0%");
    }

    [Fact]
    public async Task GetOwnershipHistory_MaxPeriodsLimitsOutput() {
        var stock = CreateStock("AAPL", "Apple Inc");
        var holder = CreateHolder();
        await SeedStockAndHolder(stock, holder);

        var dates = Enumerable.Range(0, 5)
            .Select(i => new DateOnly(2023, 3, 31).AddMonths(i * 3))
            .ToList();
        var holdings = dates.Select((d, i) =>
            CreateHolding(stock, holder, d, shares: 1000 * (i + 1),
                accessionNumber: $"0000000000-24-{i + 1:D6}")).ToList();
        _dbContext.Set<InstitutionalHolding>().AddRange(holdings);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetOwnershipHistory("AAPL", maxPeriods: 3);

        // Should only show the 3 most recent periods
        result.Should().Contain(dates[4].ToString("yyyy-MM-dd"));
        result.Should().Contain(dates[3].ToString("yyyy-MM-dd"));
        result.Should().Contain(dates[2].ToString("yyyy-MM-dd"));
        result.Should().NotContain(dates[0].ToString("yyyy-MM-dd"));
    }

    [Fact]
    public async Task GetOwnershipHistory_MultipleHoldersPerPeriod_AggregatesCorrectly() {
        var stock = CreateStock("AAPL", "Apple Inc");
        var holder1 = CreateHolder("0001", "Fund A");
        var holder2 = CreateHolder("0002", "Fund B");
        _dbContext.Set<CommonStock>().Add(stock);
        _dbContext.Set<InstitutionalHolder>().AddRange(holder1, holder2);
        await _dbContext.SaveChangesAsync();

        var reportDate = new DateOnly(2024, 3, 31);
        _dbContext.Set<InstitutionalHolding>().AddRange(
            CreateHolding(stock, holder1, reportDate, shares: 10_000, value: 1_000_000,
                accessionNumber: "0000000000-24-000001"),
            CreateHolding(stock, holder2, reportDate, shares: 5_000, value: 500_000,
                accessionNumber: "0000000000-24-000002"));
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetOwnershipHistory("AAPL");

        result.Should().Contain("15,000");
        result.Should().Contain("1.5");
    }

    // ── GetInstitutionPortfolio ──────────────────────────────────────────
    // InstitutionalHolderRepository.Search uses EF.Functions.ILike which
    // is PostgreSQL-specific and unavailable in the InMemory provider.
    // These tests use ForPartsOf<InstitutionalHolderRepository> with GetAll()
    // returning an ILikeSafeQueryable that rewrites ILike to Contains so the
    // non-virtual Search method evaluates correctly in-memory.

    [Fact]
    public async Task GetInstitutionPortfolio_HolderFoundWithHoldings_ReturnsPortfolioTable() {
        var apple = CreateStock("AAPL", "Apple Inc");
        var msft = CreateStock("MSFT", "Microsoft Corp");
        var holder = CreateHolder("0001067983", "Berkshire Hathaway Inc", "Omaha", "NE");
        _dbContext.Set<CommonStock>().AddRange(apple, msft);
        _dbContext.Set<InstitutionalHolder>().Add(holder);
        await _dbContext.SaveChangesAsync();

        var reportDate = new DateOnly(2024, 3, 31);
        _dbContext.Set<InstitutionalHolding>().AddRange(
            CreateHolding(apple, holder, reportDate, shares: 10_000, value: 2_000_000,
                accessionNumber: "0000000000-24-000001"),
            CreateHolding(msft, holder, reportDate, shares: 5_000, value: 1_500_000,
                accessionNumber: "0000000000-24-000002"));
        await _dbContext.SaveChangesAsync();

        var tools = CreateToolsWithSearchableHolders(holder);
        var result = await tools.GetInstitutionPortfolio("Berkshire");

        result.Should().Contain("Berkshire Hathaway Inc");
        result.Should().Contain("CIK: 0001067983");
        result.Should().Contain("2024-03-31");
        result.Should().Contain("AAPL");
        result.Should().Contain("Apple Inc");
        result.Should().Contain("MSFT");
        result.Should().Contain("Microsoft Corp");
    }

    [Fact]
    public async Task GetInstitutionPortfolio_HolderNotFound_ReturnsNotFoundMessage() {
        var tools = CreateToolsWithSearchableHolders();

        var result = await tools.GetInstitutionPortfolio("NonExistent");

        result.Should().Be("No institution found matching 'NonExistent'.");
    }

    [Fact]
    public async Task GetInstitutionPortfolio_HolderFoundNoHoldings_ReturnsNoDataMessage() {
        var holder = CreateHolder("0001067983", "Berkshire Hathaway Inc");
        _dbContext.Set<InstitutionalHolder>().Add(holder);
        await _dbContext.SaveChangesAsync();

        var tools = CreateToolsWithSearchableHolders(holder);
        var result = await tools.GetInstitutionPortfolio("Berkshire");

        result.Should().Contain("No holdings data for Berkshire Hathaway Inc");
    }

    [Fact]
    public async Task GetInstitutionPortfolio_WithSpecificReportDate_FiltersToThatDate() {
        var stock = CreateStock("AAPL", "Apple Inc");
        var holder = CreateHolder("0001067983", "Berkshire Hathaway Inc");
        await SeedStockAndHolder(stock, holder);

        var q1 = new DateOnly(2024, 3, 31);
        var q2 = new DateOnly(2024, 6, 30);
        _dbContext.Set<InstitutionalHolding>().AddRange(
            CreateHolding(stock, holder, q1, shares: 1_000, accessionNumber: "0000000000-24-000001"),
            CreateHolding(stock, holder, q2, shares: 2_000, accessionNumber: "0000000000-24-000002"));
        await _dbContext.SaveChangesAsync();

        var tools = CreateToolsWithSearchableHolders(holder);
        var result = await tools.GetInstitutionPortfolio("Berkshire", reportDate: "2024-03-31");

        result.Should().Contain("2024-03-31");
        result.Should().Contain("1,000");
        result.Should().NotContain("2,000");
    }

    [Fact]
    public async Task GetInstitutionPortfolio_OrdersByValueDescending() {
        var apple = CreateStock("AAPL", "Apple Inc");
        var msft = CreateStock("MSFT", "Microsoft Corp");
        var holder = CreateHolder("0001067983", "Berkshire Hathaway Inc");
        _dbContext.Set<CommonStock>().AddRange(apple, msft);
        _dbContext.Set<InstitutionalHolder>().Add(holder);
        await _dbContext.SaveChangesAsync();

        var reportDate = new DateOnly(2024, 3, 31);
        _dbContext.Set<InstitutionalHolding>().AddRange(
            CreateHolding(apple, holder, reportDate, shares: 1_000, value: 500_000,
                accessionNumber: "0000000000-24-000001"),
            CreateHolding(msft, holder, reportDate, shares: 2_000, value: 2_000_000,
                accessionNumber: "0000000000-24-000002"));
        await _dbContext.SaveChangesAsync();

        var tools = CreateToolsWithSearchableHolders(holder);
        var result = await tools.GetInstitutionPortfolio("Berkshire");

        // MSFT (value: 2M) should appear before AAPL (value: 500K)
        var msftIndex = result.IndexOf("MSFT", StringComparison.Ordinal);
        var aaplIndex = result.IndexOf("AAPL", StringComparison.Ordinal);
        msftIndex.Should().BeLessThan(aaplIndex);
    }

    // ── SearchInstitutions ──────────────────────────────────────────────
    // Same ILike workaround as GetInstitutionPortfolio above.

    [Fact]
    public async Task SearchInstitutions_MatchesFound_ReturnsFormattedTable() {
        var berkshire = CreateHolder("0001067983", "Berkshire Hathaway Inc", "Omaha", "NE");
        var blackrock = CreateHolder("0001166559", "BlackRock Inc", "New York", "NY");

        var tools = CreateToolsWithSearchableHolders(berkshire, blackrock);
        var result = await tools.SearchInstitutions("Inc");

        result.Should().Contain("Institutions matching 'Inc'");
        result.Should().Contain("Berkshire Hathaway Inc");
        result.Should().Contain("0001067983");
        result.Should().Contain("Omaha");
        result.Should().Contain("NE");
        result.Should().Contain("BlackRock Inc");
    }

    [Fact]
    public async Task SearchInstitutions_NoMatches_ReturnsNotFoundMessage() {
        var berkshire = CreateHolder("0001067983", "Berkshire Hathaway Inc");
        var tools = CreateToolsWithSearchableHolders(berkshire);

        var result = await tools.SearchInstitutions("NonExistentFund");

        result.Should().Be("No institutions found matching 'NonExistentFund'.");
    }

    [Fact]
    public async Task SearchInstitutions_MaxResultsLimitsOutput() {
        var holders = Enumerable.Range(1, 5).Select(i =>
            CreateHolder($"000{i:D7}", $"Alpha Fund {i}")).ToArray();

        var tools = CreateToolsWithSearchableHolders(holders);
        var result = await tools.SearchInstitutions("Alpha", maxResults: 3);

        result.Should().Contain("Alpha Fund 1");
        result.Should().Contain("Alpha Fund 2");
        result.Should().Contain("Alpha Fund 3");
        result.Should().NotContain("Alpha Fund 4");
        result.Should().NotContain("Alpha Fund 5");
    }

    [Fact]
    public async Task SearchInstitutions_NullCityAndState_ShowsEmDash() {
        var holder = CreateHolder("0001234567", "Mystery Fund LLC", city: null, stateOrCountry: null);

        var tools = CreateToolsWithSearchableHolders(holder);
        var result = await tools.SearchInstitutions("Mystery");

        result.Should().Contain("Mystery Fund LLC");
        // Null city and state render as em-dash
        result.Should().Contain("\u2014");
    }
}
