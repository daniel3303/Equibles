using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Repositories;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data;
using Equibles.Errors.Repositories;
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
/// Pins that <c>GetTopBuyersSellers</c> aggregates only 13F holdings when computing a filer's
/// quarter-over-quarter share delta. A filer can also file a Schedule 13D/G whose event
/// ReportDate coincides with the 13F quarter end and shares the InstitutionalHolding table; the
/// current-quarter aggregate must exclude it or the filer's "new shares" (and therefore its
/// Δ shares) is inflated by the 13D/G stake (GH-4449 named the buyers/sellers surface among
/// those the double-count inflated). <c>GetTopHolders</c> / <c>GetOwnershipHistory</c> carry the
/// sibling pins; this pins the buyers/sellers surface's per-holder share total.
/// </summary>
public class InstitutionalHoldingsToolsTopBuyersSellersExcludes13DGTests
{
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

    [Fact]
    public async Task GetTopBuyersSellers_FilerHasBoth13FAnd13GAtCurrentQuarter_DeltaUsesThe13FShareCountOnly()
    {
        await using var db = NewDb();

        var apple = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        var vanguardCapital = new InstitutionalHolder
        {
            Cik = "0002100119",
            Name = "VANGUARD CAPITAL MANAGEMENT LLC",
        };
        db.AddRange(apple, vanguardCapital);

        var priorQuarter = new DateOnly(2025, 12, 31);
        var currentQuarter = new DateOnly(2026, 3, 31);

        // Prior quarter: a clean 13F-only position to delta against.
        db.Add(
            MakeHolding(
                vanguardCapital,
                apple,
                priorQuarter,
                FilingType.Form13F,
                InvestmentDiscretion.Defined,
                shares: 900_000_000,
                value: 228_000_000_000,
                accession: "13f-hr-q4"
            )
        );
        // Current quarter: the real 13F-HR holding...
        db.Add(
            MakeHolding(
                vanguardCapital,
                apple,
                currentQuarter,
                FilingType.Form13F,
                InvestmentDiscretion.Defined,
                shares: 953_847_648,
                value: 242_077_000_000,
                accession: "13f-hr-q1"
            )
        );
        // ...and a Schedule 13G beneficial-ownership row at the same quarter end. An unfiltered
        // aggregate would add its shares to the current total, inflating the filer's Δ shares.
        db.Add(
            MakeHolding(
                vanguardCapital,
                apple,
                currentQuarter,
                FilingType.Schedule13G,
                InvestmentDiscretion.Sole,
                shares: 1_099_168_953,
                value: 278_958_100_000,
                accession: "sc-13g"
            )
        );
        await db.SaveChangesAsync();

        var sut = new InstitutionalHoldingsTools(
            new InstitutionalHoldingRepository(db),
            new InstitutionalHolderRepository(db),
            new CommonStockRepository(db),
            new StockSplitRepository(db),
            new ErrorManager(new ErrorRepository(db)),
            Substitute.For<ILogger<InstitutionalHoldingsTools>>()
        );

        var output = await sut.GetTopBuyersSellers("AAPL");

        // The current-quarter new-share total is the 13F holding alone (guards against an error
        // string and confirms the 13F-only current aggregate rendered).
        output.Should().Contain("953,847,648");
        // The 13F + 13G sum (2,053,016,601) is the inflated current total the filter must prevent.
        output.Should().NotContain("2,053,016,601");
        // The Schedule 13G sole-dispositive-power figure must never reach the delta.
        output.Should().NotContain("1,099,168,953");
    }

    private static InstitutionalHolding MakeHolding(
        InstitutionalHolder holder,
        CommonStock stock,
        DateOnly reportDate,
        FilingType filingType,
        InvestmentDiscretion discretion,
        long shares,
        long value,
        string accession
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
            InvestmentDiscretion = discretion,
            AccessionNumber = accession,
        };
}
