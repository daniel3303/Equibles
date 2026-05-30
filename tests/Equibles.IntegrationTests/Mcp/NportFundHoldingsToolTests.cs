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

public class NportFundHoldingsToolTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly NportTools _tools;

    public NportFundHoldingsToolTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new SecTestModuleConfiguration()
        );
        _tools = new NportTools(
            new NportFilingRepository(_dbContext),
            new CommonStockRepository(_dbContext),
            errorManager: null,
            NullLogger<NportTools>.Instance
        );
    }

    public void Dispose() => _dbContext.Dispose();

    private CommonStock SeedStock(string ticker = "VOO", string cik = "0000036405")
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = "Vanguard 500 Index Fund",
            Cik = cik,
        };
        _dbContext.Set<CommonStock>().Add(stock);
        _dbContext.SaveChanges();
        return stock;
    }

    [Fact]
    public async Task GetFundHoldings_StockNotFound_ReturnsNotFoundMessage()
    {
        var result = await _tools.GetFundHoldings("ZZZZ");

        result.Should().Contain("ZZZZ");
    }

    [Fact]
    public async Task GetFundHoldings_NoFilings_ReturnsEmptyMessage()
    {
        SeedStock();

        var result = await _tools.GetFundHoldings("VOO");

        result.Should().Contain("No Form NPORT-P portfolio reports found for VOO.");
    }

    [Fact]
    public async Task GetFundHoldings_UsesLatestFilingAndOrdersByValueDescending()
    {
        var stock = SeedStock();

        var older = MakeFiling(stock.Id, "older", new DateOnly(2024, 6, 30));
        older.Holdings.Add(MakeHolding("STALE CORP", 999_999m));
        _dbContext.Set<NportFiling>().Add(older);

        var latest = MakeFiling(stock.Id, "latest", new DateOnly(2025, 1, 31));
        latest.Holdings.Add(MakeHolding("SMALL CO", 100_000m));
        latest.Holdings.Add(MakeHolding("BIG CO", 5_000_000m));
        _dbContext.Set<NportFiling>().Add(latest);
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetFundHoldings("VOO");

        result.Should().Contain("Vanguard 500 Index Fund");
        result.Should().Contain("BIG CO").And.Contain("SMALL CO");
        result.Should().NotContain("STALE CORP", "only the latest filing's holdings are shown");
        // Largest holding renders before the smaller one.
        result
            .IndexOf("BIG CO", StringComparison.Ordinal)
            .Should()
            .BeLessThan(result.IndexOf("SMALL CO", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetFundHoldings_RespectsMaxResults()
    {
        var stock = SeedStock();
        var filing = MakeFiling(stock.Id, "acc", new DateOnly(2025, 1, 31));
        for (var i = 0; i < 5; i++)
            filing.Holdings.Add(MakeHolding($"CO {i}", 1_000m * (i + 1)));
        _dbContext.Set<NportFiling>().Add(filing);
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetFundHoldings("VOO", maxResults: 2);

        result.Should().Contain("showing the largest 2");
    }

    private static NportFiling MakeFiling(Guid stockId, string accession, DateOnly filingDate)
    {
        return new NportFiling
        {
            CommonStockId = stockId,
            AccessionNumber = accession,
            FilingDate = filingDate,
            IsAmendment = false,
            RegistrantName = "VANGUARD INDEX FUNDS",
            SeriesName = "Vanguard 500 Index Fund",
            SeriesId = "S000002277",
            ReportPeriodDate = filingDate.AddMonths(-1),
            ReportPeriodEnd = filingDate,
            TotalAssets = 1_200_000_000m,
            TotalLiabilities = 50_000_000m,
            NetAssets = 1_150_000_000m,
        };
    }

    private static NportHolding MakeHolding(string name, decimal valueUsd)
    {
        return new NportHolding
        {
            Name = name,
            Balance = valueUsd,
            Units = "NS",
            Currency = "USD",
            ValueUsd = valueUsd,
            PercentValue = 1.0m,
            PayoffProfile = "Long",
            AssetCategory = "EC",
            IssuerCategory = "CORP",
            InvestmentCountry = "US",
        };
    }
}
