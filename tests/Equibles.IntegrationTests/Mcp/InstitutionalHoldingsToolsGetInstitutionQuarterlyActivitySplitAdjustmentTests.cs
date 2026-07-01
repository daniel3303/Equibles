using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Data.Models;
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
/// Pins the split adjustment on <c>GetInstitutionQuarterlyActivity</c>. The tool diffs a
/// holder's positions across two report dates; when a split falls between them the two share
/// counts sit on different bases, so an economically FLAT position reads as a phantom
/// Increased move unless each side is restated onto today's basis before bucketing. This is
/// an integration test because the tool resolves the holder via a Postgres ILike name search
/// (GH-2879).
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingsToolsGetInstitutionQuarterlyActivitySplitAdjustmentTests
    : ParadeDbMcpTestBase
{
    public InstitutionalHoldingsToolsGetInstitutionQuarterlyActivitySplitAdjustmentTests(
        ParadeDbFixture fixture
    )
        : base(fixture) { }

    [Fact]
    public async Task GetInstitutionQuarterlyActivity_FlatPositionAcrossSplit_IsNotClassifiedIncreased()
    {
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
        var holder = new InstitutionalHolder { Cik = "1", Name = "Fund One Capital" };
        DbContext.AddRange(apple, microsoft, holder);

        var prior = new DateOnly(2024, 9, 30);
        var current = new DateOnly(2024, 12, 31);

        // Apple did a 2:1 split between the quarters; the fund's economic position is flat
        // (1,000 pre-split → 2,000 post-split) with a flat dollar value.
        DbContext.Add(
            new StockSplit
            {
                CommonStockId = apple.Id,
                EffectiveDate = new DateOnly(2024, 11, 15),
                Numerator = 2,
                Denominator = 1,
                Source = StockSplitSource.Yahoo,
            }
        );
        DbContext.Add(MakeHolding(holder, apple, prior, shares: 1_000, value: 100_000));
        DbContext.Add(MakeHolding(holder, apple, current, shares: 2_000, value: 100_000));

        // Microsoft has no split and a genuine share increase (1,000 → 3,000).
        DbContext.Add(MakeHolding(holder, microsoft, prior, shares: 1_000, value: 100_000));
        DbContext.Add(MakeHolding(holder, microsoft, current, shares: 3_000, value: 300_000));
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

        var output = await sut.GetInstitutionQuarterlyActivity("Fund One Capital");

        // The genuine Microsoft increase is bucketed as Increased with its real +2,000 delta.
        output.Should().Contain("## Increased");
        output.Should().Contain("MSFT");
        output.Should().Contain("+2,000");
        // The flat Apple position restates to Unchanged and is dropped — never Increased,
        // never a phantom +1,000.
        output.Should().NotContain("Apple");
        output.Should().NotContain("+1,000");
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
            FilingType = FilingType.Form13F,
            Shares = shares,
            Value = value,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = $"acc-{stock.Ticker}-{reportDate:yyyyMMdd}",
        };
}
