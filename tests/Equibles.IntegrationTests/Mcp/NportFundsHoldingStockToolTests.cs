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

public class NportFundsHoldingStockToolTests : IDisposable
{
    private const string HeldCusip = "037833100";

    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly NportTools _tools;

    public NportFundsHoldingStockToolTests()
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

    [Fact]
    public async Task GetFundsHoldingStock_StockWithoutCusip_ReturnsNoCusipMessage()
    {
        SeedStock("NOCU", cusip: null);

        var result = await _tools.GetFundsHoldingStock("NOCU");

        result.Should().Contain("No CUSIP is on record for NOCU");
    }

    [Fact]
    public async Task GetFundsHoldingStock_NoFundHoldsTheStock_ReturnsEmptyMessage()
    {
        SeedStock("AAPL", HeldCusip);

        var result = await _tools.GetFundsHoldingStock("AAPL");

        result.Should().Contain("No fund reports a position in AAPL");
    }

    [Fact]
    public async Task GetFundsHoldingStock_FundHoldsStockOnLatestReport_ReturnsThePosition()
    {
        SeedStock("AAPL", HeldCusip);
        var fund = SeedStock("VOO", cusip: null, cik: "0000036405");

        var filing = MakeFiling(fund.Id, "acc-current", new DateOnly(2025, 1, 31));
        filing.Holdings.Add(MakeHolding(HeldCusip, 5_000_000m));
        _dbContext.Set<NportFiling>().Add(filing);
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetFundsHoldingStock("AAPL");

        result.Should().Contain("VANGUARD INDEX FUNDS");
        result.Should().Contain("Vanguard 500 Index Fund");
        result.Should().Contain("1 current fund positions");
    }

    [Fact]
    public async Task GetFundsHoldingStock_PositionOnlyOnAnOlderReport_IsExcluded()
    {
        // The fund held the stock on an older report but not on its latest one — the
        // position was exited, so the reverse lookup must not show it as current.
        SeedStock("AAPL", HeldCusip);
        var fund = SeedStock("VOO", cusip: null, cik: "0000036405");

        var older = MakeFiling(fund.Id, "acc-older", new DateOnly(2024, 6, 30));
        older.Holdings.Add(MakeHolding(HeldCusip, 5_000_000m));
        _dbContext.Set<NportFiling>().Add(older);

        var latest = MakeFiling(fund.Id, "acc-latest", new DateOnly(2025, 1, 31));
        latest.Holdings.Add(MakeHolding("XXXXXXXXX", 1_000_000m));
        _dbContext.Set<NportFiling>().Add(latest);
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetFundsHoldingStock("AAPL");

        result.Should().Contain("No fund reports a position in AAPL");
    }

    [Fact]
    public async Task GetFundsHoldingStock_PositionReportedUnderRetiredCusipAlias_IsResolved()
    {
        // The stock's issuer-level CUSIP changed: the current CUSIP is on the stock, the retired one
        // is recorded as a CommonStockCusipAlias. A fund still reports the position under the OLD CUSIP
        // (a laggard filer, and every historical report forever), so the reverse lookup must resolve it
        // through the alias — mirroring the 13F import-time alias union — instead of showing the fund
        // as having exited.
        var stock = SeedStock("BBUC", cusip: "113006100");
        _dbContext
            .Set<CommonStockCusipAlias>()
            .Add(new CommonStockCusipAlias { CommonStockId = stock.Id, Cusip = "11259V106" });
        _dbContext.SaveChanges();

        var fund = SeedStock("VOO", cusip: null, cik: "0000036405");
        var filing = MakeFiling(fund.Id, "acc-current", new DateOnly(2025, 1, 31));
        filing.Holdings.Add(MakeHolding("11259V106", 5_000_000m));
        _dbContext.Set<NportFiling>().Add(filing);
        await _dbContext.SaveChangesAsync();

        var result = await _tools.GetFundsHoldingStock("BBUC");

        result.Should().Contain("VANGUARD INDEX FUNDS");
        result.Should().Contain("1 current fund positions");
    }

    private CommonStock SeedStock(string ticker, string cusip, string cik = null)
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = ticker == "VOO" ? "Vanguard 500 Index Fund" : $"{ticker} Inc.",
            Cik = cik ?? $"00009430{Math.Abs(ticker.GetHashCode()) % 100:D2}",
            Cusip = cusip,
        };
        _dbContext.Set<CommonStock>().Add(stock);
        _dbContext.SaveChanges();
        return stock;
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

    private static NportHolding MakeHolding(string cusip, decimal valueUsd)
    {
        return new NportHolding
        {
            Name = "Apple Inc.",
            Cusip = cusip,
            Balance = valueUsd / 250m,
            Units = "NS",
            Currency = "USD",
            ValueUsd = valueUsd,
            PercentValue = 0.43m,
            PayoffProfile = "Long",
            AssetCategory = "EC",
            IssuerCategory = "CORP",
            InvestmentCountry = "US",
        };
    }
}
