using System.Globalization;
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

[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingsToolsRenderOwnershipHistoryCultureInvarianceTests
    : ParadeDbMcpTestBase
{
    public InstitutionalHoldingsToolsRenderOwnershipHistoryCultureInvarianceTests(
        ParadeDbFixture fixture
    )
        : base(fixture) { }

    // RenderOwnershipHistory builds each row's Institutions / Total Shares / Total
    // Value cells with the culture-implicit :N0 / :N1 specifiers (and the change
    // column with :+0.0;-0.0), all honouring the thread CurrentCulture. The
    // established repo contract (FormatDate / FormatSignedMillions in this same
    // file thread InvariantCulture, commenting "MCP markdown must not fork the
    // separators by host locale") is that the LLM-facing markdown renders the same
    // on every host. de-DE swaps the thousand separator (1,234,567 → 1.234.567),
    // forking the response — same bug class as the fixed sibling RenderTopHoldersTable (#2628).
    [Fact]
    public async Task GetOwnershipHistory_UnderNonInvariantCulture_RendersTotalSharesCultureInvariantly()
    {
        var stock = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        var holder = new InstitutionalHolder
        {
            Cik = "0001067983",
            Name = "Berkshire Hathaway Inc.",
        };
        DbContext.Add(stock);
        DbContext.Add(holder);
        DbContext.Add(MakeHolding(stock, holder, new DateOnly(2024, 12, 31), shares: 1_234_567));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = new InstitutionalHoldingsTools(
            new InstitutionalHoldingRepository(verify),
            new InstitutionalHolderRepository(verify),
            new CommonStockRepository(verify),
            new StockSplitRepository(verify),
            new StockCombinedQuarterService(new InstitutionalHoldingRepository(verify)),
            ErrorManager,
            Substitute.For<ILogger<InstitutionalHoldingsTools>>()
        );

        // Pin de-DE only for the rendering call; CurrentCulture flows through the
        // tool's await chain via ExecutionContext. Base class restores invariant.
        var previous = CultureInfo.CurrentCulture;
        string output;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            output = await sut.GetOwnershipHistory("AAPL");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        // Numeric cells must render with en-US separators on every host locale:
        // Total Shares (:N0) and Total Value $M (:N1). de-DE would produce
        // 1.234.567 and 123,5.
        output.Should().Contain("| 1,234,567 |");
        output.Should().Contain("| 123.5 |");
    }

    private static InstitutionalHolding MakeHolding(
        CommonStock stock,
        InstitutionalHolder holder,
        DateOnly reportDate,
        long shares
    ) =>
        new()
        {
            CommonStockId = stock.Id,
            InstitutionalHolderId = holder.Id,
            FilingDate = reportDate.AddDays(45),
            ReportDate = reportDate,
            Shares = shares,
            Value = shares * 100,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = $"acc-{reportDate:yyyyMMdd}",
        };
}
