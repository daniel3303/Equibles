using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Repositories;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Adversarial cover for <c>GetInstitutionPortfolio</c>'s report-date resolution. An
/// unparseable <c>reportDate</c> must produce a clean one-line correction listing the holder's
/// available report dates — never an internal error, and never the old silent fallback to the
/// latest quarter, which let a caller asking for a historical quarter receive current data and
/// present it as historical (MCP audit 2026-07).
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingsToolsGetInstitutionPortfolioMalformedDateTests
    : ParadeDbMcpTestBase
{
    public InstitutionalHoldingsToolsGetInstitutionPortfolioMalformedDateTests(
        ParadeDbFixture fixture
    )
        : base(fixture) { }

    [Fact]
    public async Task GetInstitutionPortfolio_MalformedReportDate_ReturnsCorrectionListingDates()
    {
        var holder = new InstitutionalHolder { Cik = "1", Name = "Berkshire Hathaway Inc." };
        var apple = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        var microsoft = new CommonStock
        {
            Ticker = "MSFT",
            Name = "Microsoft Corp.",
            Cik = "0000789019",
        };
        DbContext.Add(holder);
        DbContext.Add(apple);
        DbContext.Add(microsoft);

        var olderQuarter = new DateOnly(2024, 9, 30);
        var latestQuarter = new DateOnly(2024, 12, 31);
        // Only MSFT exists in the older quarter; only AAPL in the latest.
        DbContext.Add(MakeHolding(holder, microsoft, olderQuarter, shares: 500, value: 50_000_000));
        DbContext.Add(MakeHolding(holder, apple, latestQuarter, shares: 1_000, value: 100_000_000));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = new InstitutionalHoldingsTools(
            new InstitutionalHoldingRepository(verify),
            new InstitutionalHolderRepository(verify),
            new CommonStockRepository(verify),
            new StockSplitRepository(verify),
            new StockCombinedQuarterService(
                new InstitutionalHoldingRepository(verify),
                new StockSplitRepository(verify)
            ),
            ErrorManager,
            Substitute.For<ILogger<InstitutionalHoldingsTools>>()
        );

        var output = await sut.GetInstitutionPortfolio("Berkshire", reportDate: "not-a-date");

        // A garbage date must not surface an internal error, and must not silently serve any
        // quarter's data — it returns a correction listing the holder's available dates.
        output.Should().NotContain("An error occurred while executing");
        output.Should().Contain("Could not parse reportDate 'not-a-date'");
        output.Should().Contain("2024-12-31");
        output.Should().Contain("2024-09-30");
        output.Should().NotContain("Portfolio of");
    }

    [Fact]
    public async Task GetInstitutionPortfolio_OffQuarterReportDate_SnapsToPriorReportWithNote()
    {
        var holder = new InstitutionalHolder { Cik = "1", Name = "Berkshire Hathaway Inc." };
        var apple = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        var microsoft = new CommonStock
        {
            Ticker = "MSFT",
            Name = "Microsoft Corp.",
            Cik = "0000789019",
        };
        DbContext.Add(holder);
        DbContext.Add(apple);
        DbContext.Add(microsoft);

        var olderQuarter = new DateOnly(2024, 9, 30);
        var latestQuarter = new DateOnly(2024, 12, 31);
        // Only MSFT exists in the older quarter; only AAPL in the latest.
        DbContext.Add(MakeHolding(holder, microsoft, olderQuarter, shares: 500, value: 50_000_000));
        DbContext.Add(MakeHolding(holder, apple, latestQuarter, shares: 1_000, value: 100_000_000));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = new InstitutionalHoldingsTools(
            new InstitutionalHoldingRepository(verify),
            new InstitutionalHolderRepository(verify),
            new CommonStockRepository(verify),
            new StockSplitRepository(verify),
            new StockCombinedQuarterService(
                new InstitutionalHoldingRepository(verify),
                new StockSplitRepository(verify)
            ),
            ErrorManager,
            Substitute.For<ILogger<InstitutionalHoldingsTools>>()
        );

        // A parseable mid-quarter date snaps to the nearest report ON OR BEFORE it (standard
        // as-of semantics) — 2024-11-15 serves the 2024-09-30 book, never the newer quarter —
        // and the substitution is stated in the output.
        var output = await sut.GetInstitutionPortfolio("Berkshire", reportDate: "2024-11-15");

        output.Should().NotContain("An error occurred while executing");
        output.Should().Contain("as of 2024-09-30");
        output.Should().Contain("MSFT");
        output.Should().NotContain("AAPL");
        output.Should().Contain("2024-11-15 is not a 13F report date");
    }

    private static InstitutionalHolding MakeHolding(
        InstitutionalHolder holder,
        CommonStock stock,
        DateOnly reportDate,
        long shares,
        long value
    ) =>
        new()
        {
            CommonStockId = stock.Id,
            InstitutionalHolderId = holder.Id,
            FilingDate = reportDate.AddDays(45),
            ReportDate = reportDate,
            Shares = shares,
            Value = value,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = $"acc-{stock.Ticker}",
        };
}
