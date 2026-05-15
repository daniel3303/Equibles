using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Sibling to <see cref="InstitutionalHoldingsToolsSearchTests"/>. Pins
/// <c>GetOwnershipHistory</c> — the second-most complex of the four tools.
/// The percent-change column is computed against the prior quarter's total,
/// rendered with the production format string <c>+0.0;-0.0</c>. A regression
/// that swapped the format to a default <c>F1</c> would lose the sign prefix
/// (positive changes would render without a leading '+'), silently breaking
/// MCP clients that parse the column to detect ownership trend direction.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingsToolsOwnershipHistoryTests : ParadeDbMcpTestBase
{
    public InstitutionalHoldingsToolsOwnershipHistoryTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetOwnershipHistory_QuarterOverQuarterIncrease_RendersPlusSignedPercentChange()
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

        // Q1: 1_000 shares total. Q2: 1_500 shares total → +50.0% change.
        // The format string +0.0;-0.0 must render the increase with a leading '+'.
        DbContext.Add(MakeHolding(stock, holder, new DateOnly(2024, 9, 30), shares: 1_000));
        DbContext.Add(MakeHolding(stock, holder, new DateOnly(2024, 12, 31), shares: 1_500));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = new InstitutionalHoldingsTools(
            new InstitutionalHoldingRepository(verify),
            new InstitutionalHolderRepository(verify),
            new CommonStockRepository(verify),
            ErrorManager,
            Substitute.For<ILogger<InstitutionalHoldingsTools>>()
        );

        var output = await sut.GetOwnershipHistory("AAPL");

        // First quarter has no prior — change column is "—".
        output.Should().Contain("2024-09-30");
        output.Should().Contain("| — |");
        // Second quarter must show "+50.0%" — not "50.0%" (no sign) or "+50%" (no decimal).
        output.Should().Contain("+50.0%");
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
