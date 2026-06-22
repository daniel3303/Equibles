using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
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
/// Pins that <c>GetOwnershipHistory</c> sums only 13F holdings per quarter. The same filer can
/// also have a Schedule 13D/G row whose event ReportDate coincides with the 13F quarter end and
/// shares the InstitutionalHolding table; the per-quarter "Total Shares" sum must exclude it or
/// the quarter's institutional ownership is inflated by the 13D/G stake (GH-4449 named the
/// ownership-history surface among those the double-count inflated). <c>GetTopHolders</c> carries
/// the sibling pin; this pins the ownership-history surface's share total.
/// </summary>
public class InstitutionalHoldingsToolsOwnershipHistoryExcludes13DGTests
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
                new HoldingsModuleConfiguration(),
                new ErrorsModuleConfiguration(),
            }
        );
        ctx.Database.EnsureCreated();
        return ctx;
    }

    [Fact]
    public async Task GetOwnershipHistory_FilerHasBoth13FAnd13GAtSameDate_TotalSharesExcludesThe13GRow()
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

        var quarterEnd = new DateOnly(2026, 3, 31);

        // The real 13F-HR holding — investment discretion DFND ("Defined").
        db.Add(
            MakeHolding(
                vanguardCapital,
                apple,
                quarterEnd,
                FilingType.Form13F,
                InvestmentDiscretion.Defined,
                shares: 953_847_648,
                value: 242_077_000_000,
                accession: "13f-hr"
            )
        );
        // The Schedule 13G beneficial-ownership row at the same quarter end. An unfiltered
        // per-stock query would add its shares to the quarter's total, inflating ownership.
        db.Add(
            MakeHolding(
                vanguardCapital,
                apple,
                quarterEnd,
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
            new ErrorManager(new ErrorRepository(db)),
            Substitute.For<ILogger<InstitutionalHoldingsTools>>()
        );

        var output = await sut.GetOwnershipHistory("AAPL");

        // The quarter's total reflects the 13F holding alone (guards against an error string too).
        output.Should().Contain("953,847,648");
        // The 13F + 13G sum (2,053,016,601) is the inflated total the filter must prevent.
        output.Should().NotContain("2,053,016,601");
        // The Schedule 13G sole-dispositive-power figure must never reach the share total.
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
