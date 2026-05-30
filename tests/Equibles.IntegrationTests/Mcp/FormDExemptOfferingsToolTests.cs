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
