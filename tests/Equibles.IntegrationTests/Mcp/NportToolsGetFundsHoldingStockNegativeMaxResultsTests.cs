using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Mcp.Tools;
using Equibles.Sec.Repositories;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

[Collection(ParadeDbCollection.Name)]
public class NportToolsGetFundsHoldingStockNegativeMaxResultsTests : ParadeDbMcpTestBase
{
    private const string HeldCusip = "037833100";

    public NportToolsGetFundsHoldingStockNegativeMaxResultsTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private NportTools Sut() =>
        new(
            new NportFilingRepository(DbContext),
            new CommonStockRepository(DbContext),
            new FundSeriesRepository(DbContext),
            ErrorManager,
            NullLogger<NportTools>()
        );

    [Fact]
    public async Task GetFundsHoldingStock_NegativeMaxResults_DegradesGracefullyWithoutInternalError()
    {
        // Contract: maxResults is documented as "Maximum number of fund positions to return" — a
        // ceiling on the row count, so a non-positive client value can only ever mean "return no
        // rows". The tool must degrade gracefully, never surface the generic internal-failure
        // sentinel. Sibling MCP tools route maxResults through a guard that floors it at 1
        // (GH-2931); GetFundsHoldingStock passes it straight into EF Core's .Take(maxResults), so a
        // negative value becomes a negative SQL LIMIT that PostgreSQL rejects. A real current
        // position is seeded so the query reaches .Take rather than the earlier no-data return.
        SeedStock("AAPL", HeldCusip);
        var fund = SeedStock("VOO", cusip: null, cik: "0000036405");

        var filing = MakeFiling(fund.Id, "acc-current", RecentFilingDate);
        filing.Holdings.Add(MakeHolding(HeldCusip, 5_000_000m));
        DbContext.Set<NportFiling>().Add(filing);
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetFundsHoldingStock("AAPL", maxResults: -1);

        result
            .Should()
            .NotContain(
                "An error occurred while executing GetFundsHoldingStock",
                "a client-supplied maxResults must never surface the internal-error sentinel"
            );
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
        DbContext.Set<CommonStock>().Add(stock);
        DbContext.SaveChanges();
        return stock;
    }

    // Recent relative date: GetFundsHoldingStock now applies an 18-month recency floor
    // to "current" holders, so seeded reports must stay inside it as time passes.
    private static DateOnly RecentFilingDate =>
        DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-2);

    private static NportFiling MakeFiling(Guid stockId, string accession, DateOnly filingDate) =>
        new()
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

    private static NportHolding MakeHolding(string cusip, decimal valueUsd) =>
        new()
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
