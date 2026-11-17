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
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// On a filing deadline a caller asks for the new quarter of a filer whose 13F is not on file
/// yet: the answer must say so and name the filing behind the quarter it shows instead.
/// </summary>
public class InstitutionalHoldingsToolsHolderFilingNoteTests
{
    private static readonly DateOnly Q2 = new(2026, 6, 30);

    [Fact]
    public async Task GetInstitutionPortfolio_QuarterNotOnFile_SaysSoAndNamesTheFilingShown()
    {
        await using var db = NewDb();
        var tiger = await Seed(db);

        var output = await Tools(db).GetInstitutionPortfolio(tiger.Cik, "2026-09-30");

        output.Should().Contain("as of 2026-06-30");
        output
            .Should()
            .Contain("TIGER GLOBAL MANAGEMENT LLC has no 13F for 2026-09-30 on file here");
        output.Should().NotContain("is not a 13F report date");
        output
            .Should()
            .Contain(
                "13F filing for 2026-06-30: 13F-HR filed 2026-08-14, loaded 2026-08-15 03:01 UTC (accession 0000919574-26-005427)."
            );
    }

    [Fact]
    public async Task GetInstitutionPortfolio_LatestQuarter_NamesItsFilingWithoutASnapNote()
    {
        await using var db = NewDb();
        var tiger = await Seed(db);

        var output = await Tools(db).GetInstitutionPortfolio(tiger.Cik);

        output.Should().Contain("13F filing for 2026-06-30: 13F-HR filed 2026-08-14");
        output.Should().NotContain("on file here");
    }

    private static async Task<InstitutionalHolder> Seed(EquiblesFinancialDbContext db)
    {
        EquityIssuer nvidia = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "NVDA",
            Name: "Nvidia Corp",
            Cik: "0001045810"
        );
        var tiger = new InstitutionalHolder
        {
            Cik = "1167483",
            Name = "TIGER GLOBAL MANAGEMENT LLC",
        };
        db.AddRange(nvidia, tiger);
        db.Add(
            new InstitutionalHolding
            {
                EquityIssuerId = nvidia.Id,
                InstitutionalHolderId = tiger.Id,
                FilingDate = new DateOnly(2026, 8, 14),
                ReportDate = Q2,
                FilingType = FilingType.Form13F,
                Shares = 12_000_000,
                Value = 2_740_600_000,
                ShareType = ShareType.Shares,
                InvestmentDiscretion = InvestmentDiscretion.Sole,
                AccessionNumber = "0000919574-26-005427",
            }
        );
        db.Add(
            new InstitutionalFiling
            {
                InstitutionalHolderId = tiger.Id,
                AccessionNumber = "0000919574-26-005427",
                FilingDate = new DateOnly(2026, 8, 14),
                ReportDate = Q2,
                FilingType = FilingType.Form13F,
                PositionCount = 1,
                TotalValue = 2_740_600_000,
                CreationTime = new DateTime(2026, 8, 15, 3, 1, 2, DateTimeKind.Utc),
            }
        );
        await db.SaveChangesAsync();
        return tiger;
    }

    private static InstitutionalHoldingsTools Tools(EquiblesFinancialDbContext db) =>
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
