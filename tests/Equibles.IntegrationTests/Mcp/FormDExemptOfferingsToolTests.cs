using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Mcp.Tools;
using Equibles.Sec.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.IntegrationTests.Mcp;

public class FormDExemptOfferingsToolTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly FormDTools _tools;

    public FormDExemptOfferingsToolTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new SecTestModuleConfiguration()
        );
        _tools = new FormDTools(
            new FormDFilingRepository(_dbContext),
            new CommonStockRepository(_dbContext),
            errorManager: null,
            NullLogger<FormDTools>.Instance
        );
    }

    public void Dispose() => _dbContext.Dispose();

    private CommonStock SeedStock(string ticker = "AAPL", string cik = "0000320193")
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = "Apple Inc.",
            Cik = cik,
        };
        _dbContext.Set<CommonStock>().Add(stock);
        _dbContext.SaveChanges();
        return stock;
    }

    [Fact]
    public async Task GetExemptOfferings_StockNotFound_ReturnsNotFoundMessage()
    {
        var result = await _tools.GetExemptOfferings("ZZZZ");

        result.Should().Contain("ZZZZ");
    }

    [Fact]
    public async Task GetExemptOfferings_NoFilings_ReturnsEmptyMessage()
    {
        SeedStock();

        var result = await _tools.GetExemptOfferings("AAPL");

        result.Should().Contain("No Form D exempt offerings found for AAPL.");
    }

    [Fact]
    public async Task GetExemptOfferings_WithFilings_RendersTableNewestFirst()
    {
        var stock = SeedStock();
        _dbContext
            .Set<FormDFiling>()
            .Add(MakeFiling(stock.Id, "older", new DateOnly(2025, 1, 5), offeringAmount: 1000000));
        _dbContext
            .Set<FormDFiling>()
            .Add(MakeFiling(stock.Id, "newer", new DateOnly(2025, 5, 27), offeringAmount: 5000000));
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetExemptOfferings("AAPL");

        result.Should().Contain("Apple Inc.");
        result.Should().Contain("5,000,000"); // invariant-culture grouping
        // Newest filing renders before the older one.
        result
            .IndexOf("5,000,000", StringComparison.Ordinal)
            .Should()
            .BeLessThan(result.IndexOf("1,000,000", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetExemptOfferings_IndefiniteAmount_RendersIndefinite()
    {
        var stock = SeedStock();
        _dbContext
            .Set<FormDFiling>()
            .Add(MakeFiling(stock.Id, "acc", new DateOnly(2025, 2, 28), indefinite: true));
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetExemptOfferings("AAPL");

        result.Should().Contain("Indefinite");
    }

    [Fact]
    public async Task GetExemptOfferings_RespectsMaxResults()
    {
        var stock = SeedStock();
        for (var i = 0; i < 5; i++)
        {
            _dbContext
                .Set<FormDFiling>()
                .Add(MakeFiling(stock.Id, $"acc-{i}", new DateOnly(2025, 1, 1).AddDays(i)));
        }
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetExemptOfferings("AAPL", maxResults: 2);

        result.Should().Contain("showing 2 most recent notices");
        result.Should().Contain("Showing first 2 of 5 results");
    }

    [Fact]
    public async Task GetExemptOfferings_RendersOfferingIdentityColumns()
    {
        var stock = SeedStock();
        var filing = MakeFiling(stock.Id, "0001213900-25-000001", new DateOnly(2025, 3, 1));
        filing.DateOfFirstSale = new DateOnly(2024, 11, 20);
        filing.TotalRemaining = 123_456;
        _dbContext.Set<FormDFiling>().Add(filing);
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetExemptOfferings("AAPL");

        // The chain anchor (first-sale date), the remaining amount, and the accession number
        // are what lets a consumer group D/A restatements of the same offering.
        result.Should().Contain("First Sale").And.Contain("2024-11-20");
        result.Should().Contain("Remaining").And.Contain("$123,456");
        result.Should().Contain("Accession").And.Contain("0001213900-25-000001");
    }

    [Fact]
    public async Task GetExemptOfferings_WithAmendment_AppendsChainGroupingNote()
    {
        var stock = SeedStock();
        var amendment = MakeFiling(stock.Id, "acc-a", new DateOnly(2025, 3, 1));
        amendment.IsAmendment = true;
        _dbContext.Set<FormDFiling>().Add(amendment);
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetExemptOfferings("AAPL");

        result.Should().Contain("restate a prior notice");
        result.Should().Contain("do not sum Sold across a chain");
    }

    [Fact]
    public async Task GetExemptOfferings_WithoutAmendments_OmitsChainGroupingNote()
    {
        var stock = SeedStock();
        _dbContext.Set<FormDFiling>().Add(MakeFiling(stock.Id, "acc", new DateOnly(2025, 3, 1)));
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetExemptOfferings("AAPL");

        result.Should().NotContain("restate a prior notice");
    }

    [Fact]
    public async Task GetExemptOfferings_DateRange_FiltersByFilingDate()
    {
        var stock = SeedStock();
        _dbContext
            .Set<FormDFiling>()
            .Add(MakeFiling(stock.Id, "early", new DateOnly(2025, 1, 10), offeringAmount: 111_000));
        _dbContext
            .Set<FormDFiling>()
            .Add(MakeFiling(stock.Id, "late", new DateOnly(2025, 6, 10), offeringAmount: 250_000));
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetExemptOfferings(
            "AAPL",
            fromDate: "2025-03-01",
            toDate: "2025-12-31"
        );

        result.Should().Contain("250,000");
        result.Should().NotContain("111,000");
    }

    [Fact]
    public async Task GetExemptOfferings_MalformedDate_ReturnsAcceptedFormatError()
    {
        SeedStock();

        var result = await _tools.GetExemptOfferings("AAPL", fromDate: "01/13/2025");

        result.Should().Contain("Unknown fromDate '01/13/2025'");
        result.Should().Contain("yyyy-MM-dd");
    }

    private static FormDFiling MakeFiling(
        Guid stockId,
        string accession,
        DateOnly filingDate,
        long offeringAmount = 1000000,
        bool indefinite = false
    )
    {
        return new FormDFiling
        {
            CommonStockId = stockId,
            AccessionNumber = accession,
            FilingDate = filingDate,
            IsAmendment = false,
            EntityName = "Apple Inc.",
            EntityType = "Corporation",
            JurisdictionOfInc = "CALIFORNIA",
            IndustryGroup = "Technology",
            FederalExemptions = "06b",
            TotalOfferingAmount = indefinite ? null : offeringAmount,
            IsOfferingAmountIndefinite = indefinite,
            TotalAmountSold = indefinite ? 0 : offeringAmount / 2,
            TotalRemaining = indefinite ? null : offeringAmount / 2,
            IsRemainingIndefinite = indefinite,
            MinimumInvestmentAccepted = 25000,
            TotalNumberAlreadyInvested = 10,
        };
    }
}
