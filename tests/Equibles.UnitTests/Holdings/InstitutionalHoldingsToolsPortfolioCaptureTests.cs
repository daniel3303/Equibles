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
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Equibles.UnitTests.Holdings;

// GetInstitutionPortfolio hands a host the rows its answer prints, value-ranked and paged as
// printed, with the quarter's totals, so a card beside the answer never shows other rows.
public class InstitutionalHoldingsToolsPortfolioCaptureTests
{
    private static readonly InMemoryDatabaseRoot Root = new();
    private static readonly DateOnly QuarterEnd = new(2026, 6, 30);

    [Fact]
    public async Task GetInstitutionPortfolio_RecordsThePrintedPage()
    {
        await using var db = NewDb();
        var apple = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "AAPL",
            Name: "Apple Inc.",
            Cik: "0000320193"
        );
        var coke = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "KO",
            Name: "Coca Cola Co",
            Cik: "0000021344"
        );
        var holder = new InstitutionalHolder { Cik = "1067983", Name = "Berkshire Hathaway Inc" };
        db.AddRange(apple, coke, holder);
        db.Add(Holding(holder, apple, 600, 6_000));
        db.Add(Holding(holder, coke, 300, 3_000));
        var call = Holding(holder, apple, 100, 1_000);
        call.OptionType = OptionType.Call;
        db.Add(call);
        db.Add(
            new InstitutionalFiling
            {
                InstitutionalHolderId = holder.Id,
                AccessionNumber = "0000950123-26-000001",
                FilingDate = QuarterEnd.AddDays(45),
                ReportDate = QuarterEnd,
                FilingType = FilingType.Form13F,
                PositionCount = 3,
                TotalValue = 10_000,
                DeclaredPositionCount = 4,
                DeclaredTotalValue = 12_000,
                CreationTime = new DateTime(2026, 8, 15, 3, 0, 0, DateTimeKind.Utc),
            }
        );
        await db.SaveChangesAsync();

        using var scope = McpResultCapture.Begin();
        var text = await NewTools(db).GetInstitutionPortfolio(holder.Cik, maxResults: 2);

        var capture = scope.Get<InstitutionPortfolioCapture>();
        capture.Should().NotBeNull();
        capture.Holder.Id.Should().Be(holder.Id);
        capture.Rows.Select(row => row.ListedTicker).Should().Equal("AAPL", "KO");
        capture.Rows[0].CompanyName.Should().Be("Apple Inc.");
        capture.Rows[0].Shares.Should().Be(600);
        capture.Rows[0].Value.Should().Be(6_000);
        capture.Rows[0].PercentOfPortfolio.Should().Be(60);
        capture.Rows[0].OptionType.Should().BeNull();
        capture.Rows[0].IssuerId.Should().Be(apple.Id);
        capture.ReportDate.Should().Be(QuarterEnd);
        capture.ReportDates.Should().Equal(QuarterEnd);
        capture.Offset.Should().Be(0);
        capture.TotalRows.Should().Be(3);
        capture.TotalPositions.Should().Be(2);
        capture.TotalValue.Should().Be(10_000);
        capture.DeclaredPositionCount.Should().Be(4);
        capture.DeclaredTotalValue.Should().Be(12_000);
        text.Should().Contain("| 1 | AAPL | Apple Inc. | Common | 600 | 0.0 | 60.0% |");
    }

    [Fact]
    public async Task GetInstitutionPortfolio_UnknownInstitution_RecordsNothing()
    {
        await using var db = NewDb();

        using var scope = McpResultCapture.Begin();
        await NewTools(db).GetInstitutionPortfolio("9999999");

        scope.Get<InstitutionPortfolioCapture>().Should().BeNull();
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
            NullLogger<InstitutionalHoldingsTools>.Instance
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
        long value
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
            AccessionNumber = "0000950123-26-000001",
        };
}
