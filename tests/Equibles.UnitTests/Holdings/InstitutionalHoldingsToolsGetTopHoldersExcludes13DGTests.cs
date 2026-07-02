using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Data;
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
/// Pins that <c>GetTopHolders</c> serves only 13F holdings. A filer can also have a
/// Schedule 13D/G row in the same table (beneficial-ownership disclosure) whose event
/// ReportDate coincides with the 13F quarter end — Vanguard Capital Management LLC files
/// both, so its real 13F-HR Apple holding (953,847,648, DFND) and its Schedule 13G
/// sole-dispositive-power figure (1,099,168,953) sat in the table at 2026-03-31. The
/// per-stock top-holders query did not filter by filing type, so the same filer rendered
/// twice and its institutional ownership double-counted (GH-4449). The holder-portfolio and
/// market-wide surfaces were already made 13F-only (GH-3690); this pins the remaining
/// per-stock holder surface.
/// </summary>
public class InstitutionalHoldingsToolsGetTopHoldersExcludes13DGTests
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
    public async Task GetTopHolders_FilerHasBoth13FAnd13GAtSameDate_ExcludesThe13GRow()
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
        // The Schedule 13G beneficial-ownership row — sole dispositive power, larger than the
        // 13F holding. Its ReportDate lands on the same 2026-03-31 quarter end, so an
        // unfiltered per-stock query returns it alongside the 13F holding.
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
            new StockSplitRepository(db),
            new StockCombinedQuarterService(
                new InstitutionalHoldingRepository(db),
                new StockSplitRepository(db)
            ),
            new ErrorManager(new ErrorRepository(db)),
            Substitute.For<ILogger<InstitutionalHoldingsTools>>()
        );

        var output = await sut.GetTopHolders("AAPL");

        // The real 13F holding is served (guards against a false pass on an error string).
        output.Should().Contain("953,847,648");
        // The Schedule 13G sole-dispositive-power figure must never render as a holding row.
        output.Should().NotContain("1,099,168,953");
        // The filer appears exactly once, not twice.
        CountOccurrences(output, "VANGUARD CAPITAL MANAGEMENT LLC").Should().Be(1);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
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
