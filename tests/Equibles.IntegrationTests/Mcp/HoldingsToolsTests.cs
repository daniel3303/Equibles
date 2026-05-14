using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingsToolsTests : ParadeDbMcpTestBase
{
    public InstitutionalHoldingsToolsTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private InstitutionalHoldingsTools Sut() =>
        new(
            new InstitutionalHoldingRepository(DbContext),
            new InstitutionalHolderRepository(DbContext),
            new CommonStockRepository(DbContext),
            ErrorManager,
            NullLogger<InstitutionalHoldingsTools>()
        );

    // ── Helpers ─────────────────────────────────────────────────────────

    private static CommonStock CreateStock(string ticker = "AAPL", string name = "Apple Inc")
    {
        return new CommonStock
        {
            Ticker = ticker,
            Name = name,
            Cik = Random.Shared.NextInt64(1_000_000_000L, 9_999_999_999L).ToString(),
        };
    }

    private static InstitutionalHolder CreateHolder(
        string cik = "0001067983",
        string name = "Berkshire Hathaway Inc",
        string city = "Omaha",
        string stateOrCountry = "NE"
    )
    {
        return new InstitutionalHolder
        {
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
        string accessionNumber = null
    )
    {
        return new InstitutionalHolding
        {
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

    // ── GetTopHolders ───────────────────────────────────────────────────

    [Fact]
    public async Task GetTopHolders_StockFoundWithHolders_ReturnsFormattedTable()
    {
        var stock = CreateStock("AAPL", "Apple Inc");
        var berkshire = CreateHolder("0001067983", "Berkshire Hathaway");
        var blackrock = CreateHolder("0001166559", "BlackRock Inc");
        DbContext.Set<CommonStock>().Add(stock);
        DbContext.Set<InstitutionalHolder>().AddRange(berkshire, blackrock);
        await DbContext.SaveChangesAsync();

        var reportDate = new DateOnly(2024, 3, 31);
        DbContext
            .Set<InstitutionalHolding>()
            .AddRange(
                CreateHolding(stock, berkshire, reportDate, shares: 10_000, value: 1_500_000),
                CreateHolding(stock, blackrock, reportDate, shares: 5_000, value: 750_000)
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetTopHolders("AAPL");

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
    public async Task GetTopHolders_StockNotFound_ReturnsNotFoundMessage()
    {
        var result = await Sut().GetTopHolders("ZZZZ");

        result.Should().Be("Stock 'ZZZZ' not found.");
    }

    [Fact]
    public async Task GetTopHolders_StockFoundNoHoldings_ReturnsNoDataMessage()
    {
        DbContext.Set<CommonStock>().Add(CreateStock("AAPL", "Apple Inc"));
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetTopHolders("AAPL");

        result.Should().Contain("No institutional holdings data available for AAPL");
    }

    [Fact]
    public async Task GetTopHolders_WithSpecificReportDate_FiltersToThatDate()
    {
        var stock = CreateStock("AAPL", "Apple Inc");
        var holder = CreateHolder();
        DbContext.Set<CommonStock>().Add(stock);
        DbContext.Set<InstitutionalHolder>().Add(holder);
        await DbContext.SaveChangesAsync();

        var q1 = new DateOnly(2024, 3, 31);
        var q2 = new DateOnly(2024, 6, 30);
        DbContext
            .Set<InstitutionalHolding>()
            .AddRange(
                CreateHolding(stock, holder, q1, shares: 1_000),
                CreateHolding(stock, holder, q2, shares: 2_000)
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetTopHolders("AAPL", reportDate: "2024-03-31");

        result.Should().Contain("2024-03-31");
        result.Should().Contain("1,000");
        result.Should().NotContain("2,000");
    }

    [Fact]
    public async Task GetTopHolders_DefaultsToLatestReportDate()
    {
        var stock = CreateStock("AAPL", "Apple Inc");
        var holder = CreateHolder();
        DbContext.Set<CommonStock>().Add(stock);
        DbContext.Set<InstitutionalHolder>().Add(holder);
        await DbContext.SaveChangesAsync();

        var q1 = new DateOnly(2024, 3, 31);
        var q2 = new DateOnly(2024, 6, 30);
        DbContext
            .Set<InstitutionalHolding>()
            .AddRange(
                CreateHolding(stock, holder, q1, shares: 1_000),
                CreateHolding(stock, holder, q2, shares: 2_000)
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetTopHolders("AAPL");

        result.Should().Contain("2024-06-30");
        result.Should().Contain("2,000");
    }

    [Fact]
    public async Task GetTopHolders_MaxResultsLimitsOutput()
    {
        var stock = CreateStock("AAPL", "Apple Inc");
        var holders = Enumerable
            .Range(1, 5)
            .Select(i => CreateHolder($"000{i:D7}", $"Fund {i}"))
            .ToList();
        DbContext.Set<CommonStock>().Add(stock);
        DbContext.Set<InstitutionalHolder>().AddRange(holders);
        await DbContext.SaveChangesAsync();

        var reportDate = new DateOnly(2024, 3, 31);
        var holdings = holders
            .Select((h, i) => CreateHolding(stock, h, reportDate, shares: (5 - i) * 1000))
            .ToList();
        DbContext.Set<InstitutionalHolding>().AddRange(holdings);
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetTopHolders("AAPL", maxResults: 2);

        result.Should().Contain("2 of 5 institutions");
        result.Should().Contain("Fund 1");
        result.Should().Contain("Fund 2");
        result.Should().NotContain("Fund 5");
    }

    [Fact]
    public async Task GetTopHolders_CalculatesPercentageOfTotal()
    {
        var stock = CreateStock("AAPL", "Apple Inc");
        var holder1 = CreateHolder("0001", "Fund A");
        var holder2 = CreateHolder("0002", "Fund B");
        DbContext.Set<CommonStock>().Add(stock);
        DbContext.Set<InstitutionalHolder>().AddRange(holder1, holder2);
        await DbContext.SaveChangesAsync();

        var reportDate = new DateOnly(2024, 3, 31);
        DbContext
            .Set<InstitutionalHolding>()
            .AddRange(
                CreateHolding(stock, holder1, reportDate, shares: 75_000),
                CreateHolding(stock, holder2, reportDate, shares: 25_000)
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetTopHolders("AAPL");

        result.Should().Contain("75.00%");
        result.Should().Contain("25.00%");
    }

    // ── GetOwnershipHistory ─────────────────────────────────────────────

    [Fact]
    public async Task GetOwnershipHistory_StockWithMultipleReportDates_ReturnsChronologicalTable()
    {
        var stock = CreateStock("AAPL", "Apple Inc");
        var holder = CreateHolder();
        DbContext.Set<CommonStock>().Add(stock);
        DbContext.Set<InstitutionalHolder>().Add(holder);
        await DbContext.SaveChangesAsync();

        DbContext
            .Set<InstitutionalHolding>()
            .AddRange(
                CreateHolding(
                    stock,
                    holder,
                    new DateOnly(2024, 3, 31),
                    shares: 10_000,
                    value: 1_000_000
                ),
                CreateHolding(
                    stock,
                    holder,
                    new DateOnly(2024, 6, 30),
                    shares: 12_000,
                    value: 1_200_000
                ),
                CreateHolding(
                    stock,
                    holder,
                    new DateOnly(2024, 9, 30),
                    shares: 15_000,
                    value: 1_500_000
                )
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetOwnershipHistory("AAPL");

        result.Should().Contain("Institutional ownership history for Apple Inc (AAPL)");
        result.Should().Contain("2024-03-31");
        result.Should().Contain("2024-06-30");
        result.Should().Contain("2024-09-30");
    }

    [Fact]
    public async Task GetOwnershipHistory_StockNotFound_ReturnsNotFoundMessage()
    {
        var result = await Sut().GetOwnershipHistory("ZZZZ");

        result.Should().Be("Stock 'ZZZZ' not found.");
    }

    [Fact]
    public async Task GetOwnershipHistory_StockFoundNoHistory_ReturnsNoDataMessage()
    {
        DbContext.Set<CommonStock>().Add(CreateStock("AAPL", "Apple Inc"));
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetOwnershipHistory("AAPL");

        result.Should().Contain("No institutional holdings history available for AAPL");
    }

    [Fact]
    public async Task GetOwnershipHistory_ShowsChangePercentageBetweenPeriods()
    {
        var stock = CreateStock("AAPL", "Apple Inc");
        var holder = CreateHolder();
        DbContext.Set<CommonStock>().Add(stock);
        DbContext.Set<InstitutionalHolder>().Add(holder);
        await DbContext.SaveChangesAsync();

        DbContext
            .Set<InstitutionalHolding>()
            .AddRange(
                CreateHolding(stock, holder, new DateOnly(2024, 3, 31), shares: 10_000),
                CreateHolding(stock, holder, new DateOnly(2024, 6, 30), shares: 12_000)
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetOwnershipHistory("AAPL");

        // First period has em-dash (no previous), second has +20.0%
        result.Should().Contain("—");
        result.Should().Contain("+20.0%");
    }

    [Fact]
    public async Task GetOwnershipHistory_MaxPeriodsLimitsOutput()
    {
        var stock = CreateStock("AAPL", "Apple Inc");
        var holder = CreateHolder();
        DbContext.Set<CommonStock>().Add(stock);
        DbContext.Set<InstitutionalHolder>().Add(holder);
        await DbContext.SaveChangesAsync();

        var dates = Enumerable
            .Range(0, 5)
            .Select(i => new DateOnly(2023, 3, 31).AddMonths(i * 3))
            .ToList();
        var holdings = dates
            .Select((d, i) => CreateHolding(stock, holder, d, shares: 1000 * (i + 1)))
            .ToList();
        DbContext.Set<InstitutionalHolding>().AddRange(holdings);
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetOwnershipHistory("AAPL", maxPeriods: 3);

        result.Should().Contain(dates[4].ToString("yyyy-MM-dd"));
        result.Should().Contain(dates[3].ToString("yyyy-MM-dd"));
        result.Should().Contain(dates[2].ToString("yyyy-MM-dd"));
        result.Should().NotContain(dates[0].ToString("yyyy-MM-dd"));
    }

    [Fact]
    public async Task GetOwnershipHistory_MultipleHoldersPerPeriod_AggregatesCorrectly()
    {
        var stock = CreateStock("AAPL", "Apple Inc");
        var holder1 = CreateHolder("0001", "Fund A");
        var holder2 = CreateHolder("0002", "Fund B");
        DbContext.Set<CommonStock>().Add(stock);
        DbContext.Set<InstitutionalHolder>().AddRange(holder1, holder2);
        await DbContext.SaveChangesAsync();

        var reportDate = new DateOnly(2024, 3, 31);
        DbContext
            .Set<InstitutionalHolding>()
            .AddRange(
                CreateHolding(stock, holder1, reportDate, shares: 10_000, value: 1_000_000),
                CreateHolding(stock, holder2, reportDate, shares: 5_000, value: 500_000)
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetOwnershipHistory("AAPL");

        result.Should().Contain("15,000");
        result.Should().Contain("1.5");
    }

    // ── GetInstitutionPortfolio ──────────────────────────────────────────
    // InstitutionalHolderRepository.Search uses EF.Functions.ILike which now runs
    // natively against the ParadeDB container — no test-only ILike shim required.

    [Fact]
    public async Task GetInstitutionPortfolio_HolderFoundWithHoldings_ReturnsPortfolioTable()
    {
        var apple = CreateStock("AAPL", "Apple Inc");
        var msft = CreateStock("MSFT", "Microsoft Corp");
        var holder = CreateHolder("0001067983", "Berkshire Hathaway Inc", "Omaha", "NE");
        DbContext.Set<CommonStock>().AddRange(apple, msft);
        DbContext.Set<InstitutionalHolder>().Add(holder);
        await DbContext.SaveChangesAsync();

        var reportDate = new DateOnly(2024, 3, 31);
        DbContext
            .Set<InstitutionalHolding>()
            .AddRange(
                CreateHolding(apple, holder, reportDate, shares: 10_000, value: 2_000_000),
                CreateHolding(msft, holder, reportDate, shares: 5_000, value: 1_500_000)
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetInstitutionPortfolio("Berkshire");

        result.Should().Contain("Berkshire Hathaway Inc");
        result.Should().Contain("CIK: 0001067983");
        result.Should().Contain("2024-03-31");
        result.Should().Contain("AAPL");
        result.Should().Contain("Apple Inc");
        result.Should().Contain("MSFT");
        result.Should().Contain("Microsoft Corp");
    }

    [Fact]
    public async Task GetInstitutionPortfolio_HolderNotFound_ReturnsNotFoundMessage()
    {
        var result = await Sut().GetInstitutionPortfolio("NonExistent");

        result.Should().Be("No institution found matching 'NonExistent'.");
    }

    [Fact]
    public async Task GetInstitutionPortfolio_HolderFoundNoHoldings_ReturnsNoDataMessage()
    {
        DbContext
            .Set<InstitutionalHolder>()
            .Add(CreateHolder("0001067983", "Berkshire Hathaway Inc"));
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetInstitutionPortfolio("Berkshire");

        result.Should().Contain("No holdings data for Berkshire Hathaway Inc");
    }

    [Fact]
    public async Task GetInstitutionPortfolio_WithSpecificReportDate_FiltersToThatDate()
    {
        var stock = CreateStock("AAPL", "Apple Inc");
        var holder = CreateHolder("0001067983", "Berkshire Hathaway Inc");
        DbContext.Set<CommonStock>().Add(stock);
        DbContext.Set<InstitutionalHolder>().Add(holder);
        await DbContext.SaveChangesAsync();

        var q1 = new DateOnly(2024, 3, 31);
        var q2 = new DateOnly(2024, 6, 30);
        DbContext
            .Set<InstitutionalHolding>()
            .AddRange(
                CreateHolding(stock, holder, q1, shares: 1_000),
                CreateHolding(stock, holder, q2, shares: 2_000)
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetInstitutionPortfolio("Berkshire", reportDate: "2024-03-31");

        result.Should().Contain("2024-03-31");
        result.Should().Contain("1,000");
        result.Should().NotContain("2,000");
    }

    [Fact]
    public async Task GetInstitutionPortfolio_OrdersByValueDescending()
    {
        var apple = CreateStock("AAPL", "Apple Inc");
        var msft = CreateStock("MSFT", "Microsoft Corp");
        var holder = CreateHolder("0001067983", "Berkshire Hathaway Inc");
        DbContext.Set<CommonStock>().AddRange(apple, msft);
        DbContext.Set<InstitutionalHolder>().Add(holder);
        await DbContext.SaveChangesAsync();

        var reportDate = new DateOnly(2024, 3, 31);
        DbContext
            .Set<InstitutionalHolding>()
            .AddRange(
                CreateHolding(apple, holder, reportDate, shares: 1_000, value: 500_000),
                CreateHolding(msft, holder, reportDate, shares: 2_000, value: 2_000_000)
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetInstitutionPortfolio("Berkshire");

        // MSFT (value 2M) outranks AAPL (value 500K).
        result
            .IndexOf("MSFT", StringComparison.Ordinal)
            .Should()
            .BeLessThan(result.IndexOf("AAPL", StringComparison.Ordinal));
    }

    // ── SearchInstitutions ──────────────────────────────────────────────

    [Fact]
    public async Task SearchInstitutions_MatchesFound_ReturnsFormattedTable()
    {
        DbContext
            .Set<InstitutionalHolder>()
            .AddRange(
                CreateHolder("0001067983", "Berkshire Hathaway Inc", "Omaha", "NE"),
                CreateHolder("0001166559", "BlackRock Inc", "New York", "NY")
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut().SearchInstitutions("Inc");

        result.Should().Contain("Institutions matching 'Inc'");
        result.Should().Contain("Berkshire Hathaway Inc");
        result.Should().Contain("0001067983");
        result.Should().Contain("Omaha");
        result.Should().Contain("NE");
        result.Should().Contain("BlackRock Inc");
    }

    [Fact]
    public async Task SearchInstitutions_NoMatches_ReturnsNotFoundMessage()
    {
        DbContext
            .Set<InstitutionalHolder>()
            .Add(CreateHolder("0001067983", "Berkshire Hathaway Inc"));
        await DbContext.SaveChangesAsync();

        var result = await Sut().SearchInstitutions("NonExistentFund");

        result.Should().Be("No institutions found matching 'NonExistentFund'.");
    }

    [Fact]
    public async Task SearchInstitutions_MaxResultsLimitsOutput()
    {
        var holders = Enumerable
            .Range(1, 5)
            .Select(i => CreateHolder($"000{i:D7}", $"Alpha Fund {i}"))
            .ToArray();
        DbContext.Set<InstitutionalHolder>().AddRange(holders);
        await DbContext.SaveChangesAsync();

        var result = await Sut().SearchInstitutions("Alpha", maxResults: 3);

        result.Should().Contain("Alpha Fund 1");
        result.Should().Contain("Alpha Fund 2");
        result.Should().Contain("Alpha Fund 3");
        result.Should().NotContain("Alpha Fund 4");
        result.Should().NotContain("Alpha Fund 5");
    }

    [Fact]
    public async Task SearchInstitutions_NullCityAndState_ShowsEmDash()
    {
        DbContext
            .Set<InstitutionalHolder>()
            .Add(CreateHolder("0001234567", "Mystery Fund LLC", city: null, stateOrCountry: null));
        await DbContext.SaveChangesAsync();

        var result = await Sut().SearchInstitutions("Mystery");

        result.Should().Contain("Mystery Fund LLC");
        result.Should().Contain("—");
    }

    [Fact]
    public async Task SearchInstitutions_IsCaseInsensitive()
    {
        DbContext
            .Set<InstitutionalHolder>()
            .Add(CreateHolder("0001067983", "Berkshire Hathaway Inc"));
        await DbContext.SaveChangesAsync();

        // Production code uses EF.Functions.ILike — this only succeeds against real Postgres.
        var result = await Sut().SearchInstitutions("berkshire");

        result.Should().Contain("Berkshire Hathaway Inc");
    }
}
