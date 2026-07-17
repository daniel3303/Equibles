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
            new FundSeriesRepository(_dbContext),
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
        result.Should().Contain("Showing first 2 of 5 results");
    }

    [Fact]
    public async Task GetFundHoldings_LateFiledAmendmentOfOlderPeriod_DoesNotShadowNewestPeriod()
    {
        var stock = SeedStock();

        var current = MakeFiling(stock.Id, "current", new DateOnly(2025, 3, 15));
        current.ReportPeriodDate = new DateOnly(2025, 2, 28);
        current.Holdings.Add(MakeHolding("CURRENT CO", 1_000_000m));
        _dbContext.Set<NportFiling>().Add(current);

        // Amendment of an OLDER period filed AFTER the current period's report: picking by
        // filing date alone would surface this stale-period portfolio as "most recent".
        var amendment = MakeFiling(stock.Id, "amendment", new DateOnly(2025, 4, 1));
        amendment.ReportPeriodDate = new DateOnly(2024, 12, 31);
        amendment.IsAmendment = true;
        amendment.Holdings.Add(MakeHolding("AMENDED CO", 2_000_000m));
        _dbContext.Set<NportFiling>().Add(amendment);
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetFundHoldings("VOO");

        result.Should().Contain("CURRENT CO");
        result.Should().Contain("2025-02-28");
        result.Should().NotContain("AMENDED CO");
    }

    [Fact]
    public async Task GetFundHoldings_GlossesUnitAndCategoryCodesAndDropsWholeShareDecimals()
    {
        var stock = SeedStock();
        var filing = MakeFiling(stock.Id, "acc", new DateOnly(2025, 1, 31));
        filing.Holdings.Add(MakeHolding("BIG CO", 5_000_000m));
        _dbContext.Set<NportFiling>().Add(filing);
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetFundHoldings("VOO");

        result.Should().Contain("NS (shares)");
        result.Should().Contain("EC (equity-common)");
        result.Should().Contain("| 5,000,000 |", "whole share balances carry no .00 noise");
        result.Should().NotContain("| 5,000,000.00 |");
    }

    [Fact]
    public async Task GetFundHoldings_TickerlessTrustSeries_ResolvesThroughFundDirectorySlug()
    {
        SeedTrustSeries();
        _dbContext.Set<NportFiling>().Add(MakeTrustFiling("acc-1", new DateOnly(2026, 5, 15)));
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetFundHoldings("vanguard-500-index-fund-s000002839");

        result.Should().Contain("VANGUARD 500 INDEX FUND");
        result.Should().Contain("TRACKED CO");
    }

    [Fact]
    public async Task GetFundHoldings_SeriesLevelTicker_ResolvesThroughFundDirectory()
    {
        SeedTrustSeries(ticker: "VFV");
        _dbContext.Set<NportFiling>().Add(MakeTrustFiling("acc-1", new DateOnly(2026, 5, 15)));
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetFundHoldings("vfv");

        result.Should().Contain("VANGUARD 500 INDEX FUND");
        result.Should().Contain("TRACKED CO");
    }

    [Fact]
    public async Task GetFundHoldings_UnknownIdentifier_PointsAtSearchFunds()
    {
        var result = await _tools.GetFundHoldings("VOO");

        result.Should().Contain("No fund or ETF found for 'VOO'");
        result.Should().Contain("SearchFunds");
    }

    private void SeedTrustSeries(string ticker = null)
    {
        _dbContext
            .Set<FundSeries>()
            .Add(
                new FundSeries
                {
                    IdentityKey = "rc:0000102909:S000002839",
                    Slug = "vanguard-500-index-fund-s000002839",
                    RegistrantCik = "0000102909",
                    SeriesId = "S000002839",
                    SeriesName = "VANGUARD 500 INDEX FUND",
                    RegistrantName = "VANGUARD INDEX FUNDS",
                    Ticker = ticker,
                    LatestReportPeriodDate = new DateOnly(2026, 3, 31),
                    LatestFilingDate = new DateOnly(2026, 5, 15),
                    NetAssets = 1_400_000_000_000m,
                    TotalAssets = 1_450_000_000_000m,
                    PositionCount = 1,
                }
            );
        _dbContext.SaveChanges();
    }

    private static NportFiling MakeTrustFiling(string accession, DateOnly filingDate)
    {
        var filing = new NportFiling
        {
            CommonStockId = null,
            RegistrantCik = "0000102909",
            AccessionNumber = accession,
            FilingDate = filingDate,
            IsAmendment = false,
            RegistrantName = "VANGUARD INDEX FUNDS",
            SeriesName = "VANGUARD 500 INDEX FUND",
            SeriesId = "S000002839",
            ReportPeriodDate = new DateOnly(2026, 3, 31),
            ReportPeriodEnd = filingDate,
            TotalAssets = 1_450_000_000_000m,
            TotalLiabilities = 50_000_000_000m,
            NetAssets = 1_400_000_000_000m,
        };
        filing.Holdings.Add(MakeHolding("TRACKED CO", 10_000_000m));
        return filing;
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
