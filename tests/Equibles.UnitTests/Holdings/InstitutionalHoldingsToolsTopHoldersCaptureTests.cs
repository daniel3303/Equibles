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
using Equibles.Holdings.Mcp.Captures;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories;
using Equibles.Mcp;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.UnitTests.Holdings;

// GetTopHolders hands a host the rows its answer prints, ranked and paged as printed, with the
// totals over every institution, so a card beside the answer never shows other rows or figures.
public class InstitutionalHoldingsToolsTopHoldersCaptureTests
{
    private static readonly InMemoryDatabaseRoot Root = new();
    private static readonly DateOnly QuarterEnd = new(2026, 6, 30);

    [Fact]
    public async Task GetTopHolders_RecordsThePrintedPage()
    {
        await using var db = NewDb();
        var stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "NVDA",
            Name: "Nvidia Corp",
            Cik: "0001045810"
        );
        var large = new InstitutionalHolder { Cik = "0002012383", Name = "Large Holder" };
        var middle = new InstitutionalHolder { Cik = "0000093751", Name = "Middle Holder" };
        var small = new InstitutionalHolder { Cik = "0000315066", Name = "Small Holder" };
        db.AddRange(stock, large, middle, small);
        db.Add(Holding(large, stock, 600, 6_000, "a"));
        db.Add(Holding(small, stock, 100, 1_000, "c"));
        var call = Holding(middle, stock, 300, 3_000, "b");
        call.OptionType = OptionType.Call;
        db.Add(call);
        await db.SaveChangesAsync();

        using var scope = McpResultCapture.Begin();
        var text = await NewTools(db).GetTopHolders("NVDA", maxResults: 2);

        var capture = scope.Get<TopHoldersCapture>();
        capture.Should().NotBeNull();
        capture.Rows.Select(row => row.Name).Should().Equal("Large Holder", "Middle Holder");
        capture.Rows[0].Cik.Should().Be("0002012383");
        capture.Rows[0].Shares.Should().Be(600);
        capture.Rows[0].PercentOfTotal.Should().Be(60);
        capture.Rows[1].OptionType.Should().Be(OptionType.Call);
        capture.Rows.Should().OnlyContain(row => !row.Carried && !row.Withheld);
        capture.ReportDate.Should().Be(QuarterEnd);
        capture.CarriedFrom.Should().BeNull();
        capture.ReportDates.Should().Equal(QuarterEnd);
        capture.TotalRows.Should().Be(3);
        capture.TotalInstitutions.Should().Be(3);
        capture.TotalShares.Should().Be(1_000);
        capture.TotalValue.Should().Be(10_000);
        capture.CallShares.Should().Be(300);
        capture.PutShares.Should().Be(0);
        capture.Stock.Id.Should().Be(stock.Id);
        text.Should().Contain("60.00%");
    }

    [Fact]
    public async Task GetTopHolders_UnknownTicker_RecordsNothing()
    {
        await using var db = NewDb();

        using var scope = McpResultCapture.Begin();
        await NewTools(db).GetTopHolders("ZZZZ");

        scope.Get<TopHoldersCapture>().Should().BeNull();
    }

    private static InstitutionalHoldingsTools NewTools(EquiblesFinancialDbContext db) =>
        new(
            new InstitutionalHoldingRepository(db),
            new InstitutionalHolderRepository(db),
            new EquityIssuerRepository(db),
            new StockSplitRepository(db),
            new StockCombinedQuarterService(
                new InstitutionalHoldingRepository(db),
                new StockSplitRepository(db)
            ),
            new ErrorManager(new ErrorRepository(db)),
            Substitute.For<ILogger<InstitutionalHoldingsTools>>()
        );

    private static EquiblesFinancialDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), Root)
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

    private static InstitutionalHolding Holding(
        InstitutionalHolder holder,
        EquityIssuer stock,
        long shares,
        long value,
        string accession
    ) =>
        new()
        {
            EquityIssuerId = stock.Id,
            InstitutionalHolderId = holder.Id,
            FilingDate = QuarterEnd.AddDays(45),
            ReportDate = QuarterEnd,
            FilingType = FilingType.Form13F,
            Shares = shares,
            Value = value,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = accession,
        };
}
