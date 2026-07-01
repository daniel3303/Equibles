using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Repositories;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Pins that <c>GetInstitutionPortfolio</c> resolves its default report date from 13F
/// quarters only. A Schedule 13D/G filed AFTER the last 13F quarter carries a daily
/// event date, not a quarter end, and describes a single disclosed stake — if the tool
/// resolved "latest" from all filing types, the served portfolio would collapse into that
/// one stock at the event date instead of the real 13F holdings. Same root cause as
/// GH-3685 (fund scoring / smart-money index), fixed for the remaining holder-portfolio
/// surfaces in GH-3690.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingsToolsGetInstitutionPortfolioExcludes13DGTests
    : ParadeDbMcpTestBase
{
    public InstitutionalHoldingsToolsGetInstitutionPortfolioExcludes13DGTests(
        ParadeDbFixture fixture
    )
        : base(fixture) { }

    [Fact]
    public async Task GetInstitutionPortfolio_Later13DGEventDate_ServesLatest13FQuarterNotTheStake()
    {
        var holder = new InstitutionalHolder { Cik = "1", Name = "Pinnacle Capital Management" };
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
        var tesla = new CommonStock
        {
            Ticker = "TSLA",
            Name = "Tesla Inc.",
            Cik = "0001318605",
        };
        DbContext.AddRange(holder, apple, microsoft, tesla);

        var quarterEnd = new DateOnly(2026, 3, 31);
        // A single-stock Schedule 13D filed AFTER the latest 13F quarter — exactly the row
        // that would hijack "latest" and replace the real portfolio with one stake.
        var event13D = new DateOnly(2026, 5, 20);
        DbContext.Add(MakeHolding(holder, apple, quarterEnd, FilingType.Form13F, 1_000, 200_000));
        DbContext.Add(
            MakeHolding(holder, microsoft, quarterEnd, FilingType.Form13F, 2_000, 300_000)
        );
        DbContext.Add(MakeHolding(holder, tesla, event13D, FilingType.Schedule13D, 5_000, 900_000));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = new InstitutionalHoldingsTools(
            new InstitutionalHoldingRepository(verify),
            new InstitutionalHolderRepository(verify),
            new CommonStockRepository(verify),
            new StockSplitRepository(verify),
            ErrorManager,
            Substitute.For<ILogger<InstitutionalHoldingsTools>>()
        );

        var output = await sut.GetInstitutionPortfolio("Pinnacle");

        // The default date must be the 2026-03-31 13F quarter (AAPL + MSFT), never the
        // 2026-05-20 13D event date (TSLA stake at 100%).
        output.Should().Contain("2026-03-31");
        output.Should().Contain("AAPL");
        output.Should().Contain("MSFT");
        output.Should().NotContain("TSLA");
        output.Should().NotContain("2026-05-20");
    }

    private static InstitutionalHolding MakeHolding(
        InstitutionalHolder holder,
        CommonStock stock,
        DateOnly reportDate,
        FilingType filingType,
        long shares,
        long value
    ) =>
        new()
        {
            CommonStockId = stock.Id,
            InstitutionalHolderId = holder.Id,
            FilingDate = reportDate.AddDays(45),
            ReportDate = reportDate,
            FilingType = filingType,
            Shares = shares,
            Value = value,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = $"acc-{stock.Ticker}",
        };
}
