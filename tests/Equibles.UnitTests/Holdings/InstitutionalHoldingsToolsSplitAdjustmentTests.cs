using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data;
using Equibles.Errors.Repositories;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Pins the split adjustment on the cross-sectional / per-holder 13F activity tools. These
/// diff share counts across two report dates; when a split falls between the quarters the two
/// counts sit on different bases, so an economically FLAT position reads as a phantom buyer /
/// Increased move unless each side is restated onto today's basis first. Δ Value is a dollar
/// figure and is split-invariant, so ranking by it is left alone (GH-2879).
/// </summary>
public class InstitutionalHoldingsToolsSplitAdjustmentTests
{
    private static readonly DateOnly Prior = new(2024, 9, 30);
    private static readonly DateOnly Current = new(2024, 12, 31);

    // 2:1 forward split effective between the two report dates.
    private static readonly DateOnly SplitDate = new(2024, 11, 15);

    private static EquiblesFinancialDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableServiceProviderCaching(false)
            .Options;
        var ctx = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new CorporateActionsModuleConfiguration(),
                new HoldingsModuleConfiguration(),
                new ErrorsModuleConfiguration(),
            }
        );
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static InstitutionalHoldingsTools Sut(EquiblesFinancialDbContext db) =>
        new(
            new InstitutionalHoldingRepository(db),
            new InstitutionalHolderRepository(db),
            new CommonStockRepository(db),
            new StockSplitRepository(db),
            new StockCombinedQuarterService(new InstitutionalHoldingRepository(db)),
            new ErrorManager(new ErrorRepository(db)),
            Substitute.For<ILogger<InstitutionalHoldingsTools>>()
        );

    [Fact]
    public async Task GetMarketWide13FActivity_FlatPositionAcrossSplit_IsNotAPhantomTopBuyer()
    {
        await using var db = NewDb();

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
        var holder = new InstitutionalHolder { Cik = "1", Name = "Fund One" };
        db.AddRange(apple, microsoft, holder);

        // Apple did a 2:1 split between the quarters; the fund's economic position is flat
        // (1,000 pre-split → 2,000 post-split) with a flat dollar value.
        db.Add(
            new StockSplit
            {
                CommonStockId = apple.Id,
                EffectiveDate = SplitDate,
                Numerator = 2,
                Denominator = 1,
                Source = StockSplitSource.Yahoo,
            }
        );
        db.Add(MakeHolding(holder, apple, Prior, shares: 1_000, value: 100_000));
        db.Add(MakeHolding(holder, apple, Current, shares: 2_000, value: 100_000));

        // Microsoft has no split and a genuine share increase (1,000 → 3,000).
        db.Add(MakeHolding(holder, microsoft, Prior, shares: 1_000, value: 100_000));
        db.Add(MakeHolding(holder, microsoft, Current, shares: 3_000, value: 300_000));
        await db.SaveChangesAsync();

        var output = await Sut(db).GetMarketWide13FActivity("top-buys");

        // The genuine Microsoft buyer surfaces with its real +2,000 delta.
        output.Should().Contain("MSFT");
        output.Should().Contain("+2,000");
        // The flat Apple position must not appear as a phantom buyer (its raw +1,000 delta
        // is a split artifact that restatement cancels).
        output.Should().NotContain("Apple");
        output.Should().NotContain("+1,000");
    }

    // GetInstitutionQuarterlyActivity resolves the holder via a Postgres ILike name search,
    // so its split-adjustment regression lives in the Docker-backed integration suite
    // (InstitutionalHoldingsToolsGetInstitutionQuarterlyActivitySplitAdjustmentTests).

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
