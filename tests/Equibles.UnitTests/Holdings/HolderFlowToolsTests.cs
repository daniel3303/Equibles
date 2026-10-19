using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models.Taxonomies;
using Equibles.CorporateActions.Data;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data;
using Equibles.Errors.Repositories;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.BusinessLogic.Intelligence;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Equibles.UnitTests.Holdings;

public class HolderFlowToolsTests
{
    private static readonly DateOnly Prior = new(2026, 3, 31);
    private static readonly DateOnly Latest = new(2026, 6, 30);

    [Fact]
    public async Task GetHolderTypeFlows_TabulatesEachBucketAndTheTotal()
    {
        await using var db = NewDb();
        await Seed(db);

        var output = await Tools(db).GetHolderTypeFlows();

        output
            .Should()
            .Contain("13F holder flows by holder type, quarter ended 2026-06-30 vs 2026-03-31");
        output
            .Should()
            .Contain("| Registered adviser | $300 | $100 | +$200 | +20.0% | 1 of 1 | 1 / 0 |");
        output.Should().Contain("| Bank | $0 | $0 | $0 | — | 0 of 1 | 0 / 0 |");
        output
            .Should()
            .Contain("| All 13F filers | $300 | $100 | +$200 | +20.0% | 1 of 2 | 1 / 0 |");
        output
            .Should()
            .Contain("1 of 2 filers had a 13F the quarter before and are compared; 1 add no flow.");
    }

    [Fact]
    public async Task GetHolderTypeFlows_BySector_TabulatesSectorsAcrossBuckets()
    {
        await using var db = NewDb();
        await Seed(db);

        var matrix = await Tools(db).GetHolderTypeFlows(groupBy: "SECTOR");
        var adviser = await Tools(db).GetHolderTypeFlows(bucket: "adv");
        var unknown = await Tools(db).GetHolderTypeFlows(bucket: "hedge");

        matrix.Should().Contain("| Sector | Registered adviser | Bank | All 13F filers |");
        matrix.Should().Contain("| Technology | +$250 | — | +$250 |");
        matrix.Should().Contain("| Exchange-traded products | -$50 | — | -$50 |");
        matrix.Should().Contain("| All sectors | +$200 | $0 | +$200 |");
        adviser.Should().Contain("13F flows of Registered adviser by sector");
        adviser.Should().Contain("| Technology | $250 | $0 | +$250 |");
        adviser.Should().Contain("| All sectors | $300 | $100 | +$200 |");
        unknown
            .Should()
            .Be("Unknown bucket 'hedge' for holder type in 2026-06-30. Use one of: adv, bank.");
    }

    [Fact]
    public async Task GetHolderTypeFlows_AQuarterWithoutSectorRowsSaysSo()
    {
        await using var db = NewDb();
        await Seed(db, latestVersion: HolderSectorFlowCalculator.FirstVersion - 1);

        var output = await Tools(db).GetHolderTypeFlows(groupBy: "sector");

        output.Should().Contain("The sector split of 2026-06-30 is not computed yet");
        output.Should().Contain("| Registered adviser | $300 | $100 | +$200 |");
    }

    [Fact]
    public async Task GetHolderTypeFlows_ResolvesTheReportDate()
    {
        await using var db = NewDb();
        await Seed(db);

        var open = await Tools(db).GetHolderTypeFlows(reportDate: "2026-09-30");
        var offQuarter = await Tools(db).GetHolderTypeFlows(reportDate: "2026-05-15");
        var tooEarly = await Tools(db).GetHolderTypeFlows(reportDate: "2025-12-31");
        var malformed = await Tools(db).GetHolderTypeFlows(reportDate: "06/30/2026");

        open.Should().Contain("2026-09-30 is not computed yet").And.Contain("showing 2026-06-30");
        offQuarter
            .Should()
            .Contain("2026-05-15 is not a computed quarter end; showing 2026-03-31.");
        tooEarly
            .Should()
            .Be(
                "No holder flows before 2026-03-31; computed quarters run 2026-03-31 to 2026-06-30."
            );
        malformed.Should().Contain("reportDate");
    }

    [Fact]
    public async Task GetHolderTypeFlows_RefusesUnknownTokensAndReportsNothingComputed()
    {
        await using var db = NewDb();

        var none = await Tools(db).GetHolderTypeFlows();
        var dimension = await Tools(db).GetHolderTypeFlows(dimension: "sector");
        var grouping = await Tools(db).GetHolderTypeFlows(groupBy: "industry");

        none.Should().StartWith("Holder flows are not computed yet");
        dimension.Should().StartWith("Unknown dimension 'sector'.");
        grouping.Should().Be("Unknown groupBy 'industry'. Use one of: none, sector.");
    }

    // Two filers measured in the latest quarter: an adviser compared with the quarter before and a
    // bank with no prior 13F. The prior quarter is computed but carries no trades.
    private static async Task Seed(
        EquiblesFinancialDbContext db,
        int latestVersion = HolderSectorFlowCalculator.FirstVersion
    )
    {
        var technology = new Sector { Name = "Technology" };
        var adviser = new InstitutionalHolder { Name = "Adviser", Cik = "100" };
        var bank = new InstitutionalHolder { Name = "Bank", Cik = "200" };
        db.AddRange(technology, adviser, bank);
        db.AddRange(
            Profile(adviser, HolderEntityType.RegisteredAdviser),
            Profile(bank, HolderEntityType.Bank),
            new HolderIntelligenceQuarterState
            {
                ReportDate = Prior,
                ComputedAt = new DateTime(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc),
                CalculationVersion = HolderSectorFlowCalculator.FirstVersion,
            },
            new HolderIntelligenceQuarterState
            {
                ReportDate = Latest,
                ComputedAt = new DateTime(2026, 10, 19, 0, 0, 0, DateTimeKind.Utc),
                CalculationVersion = latestVersion,
            },
            new HolderQuarterlyMetrics
            {
                InstitutionalHolderId = adviser.Id,
                ReportDate = Prior,
                EquityAum = 1_000,
            },
            new HolderQuarterlyMetrics
            {
                InstitutionalHolderId = adviser.Id,
                ReportDate = Latest,
                EquityAum = 1_200,
                PreviousEquityAum = 1_000,
                BuyValue = 300m,
                SellValue = 100m,
                UnpricedTradeCount = 0,
            },
            new HolderQuarterlyMetrics
            {
                InstitutionalHolderId = bank.Id,
                ReportDate = Latest,
                EquityAum = 800,
            },
            new HolderQuarterlySectorFlow
            {
                InstitutionalHolderId = adviser.Id,
                ReportDate = Latest,
                SectorGroup = HolderFlowSectorGroup.Sector,
                SectorId = technology.Id,
                BuyValue = 250m,
            },
            new HolderQuarterlySectorFlow
            {
                InstitutionalHolderId = adviser.Id,
                ReportDate = Latest,
                SectorGroup = HolderFlowSectorGroup.ExchangeTraded,
                BuyValue = 50m,
                SellValue = 100m,
            }
        );
        await db.SaveChangesAsync();
    }

    private static InstitutionalHolderProfile Profile(
        InstitutionalHolder holder,
        HolderEntityType type
    ) => new() { InstitutionalHolderId = holder.Id, EntityType = type };

    private static HolderFlowTools Tools(EquiblesFinancialDbContext db) =>
        new(
            new HolderFlowReader(new(db), new(db), new MemoryCache(new MemoryCacheOptions())),
            new ErrorManager(new ErrorRepository(db)),
            NullLogger<HolderFlowTools>.Instance
        );

    private static EquiblesFinancialDbContext NewDb()
    {
        var db = new EquiblesFinancialDbContext(
            new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .EnableServiceProviderCaching(false)
                .Options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new CorporateActionsModuleConfiguration(),
                new HoldingsModuleConfiguration(),
                new ErrorsModuleConfiguration(),
            }
        );
        db.Database.EnsureCreated();
        return db;
    }
}
