using Equibles.CommonStocks.Data;
using Equibles.CorporateActions.Data;
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
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Equibles.UnitTests.Holdings;

public class LatestFilingsToolsTests
{
    private static readonly DateOnly Q2 = new(2026, 6, 30);
    private static readonly DateOnly Q3 = new(2026, 9, 30);

    [Fact]
    public async Task GetLatest13FFilings_ListsNewestFilingDayFirstWithFiledAndLoadedDates()
    {
        await using var db = NewDb();
        await Seed(db);

        var output = await Tools(db).GetLatest13FFilings();

        output
            .Should()
            .Contain("Loaded by SEC filing day: 2026-11-16: 2; 2026-11-12: 1; 2026-11-05: 1.");
        output.Should().Contain("Newest load among these filing days: 2026-11-17 12:42 UTC.");
        output
            .Should()
            .Contain(
                "| 2 | TIGER GLOBAL MANAGEMENT LLC | 1167483 | 2026-09-30 | 2026-11-16 | 2026-11-17 12:42 | 13F-HR | 45 | 20,173.1 |"
            );
        output
            .Should()
            .Contain(
                "| 3 | RENAISSANCE TECHNOLOGIES LLC | 1037389 | 2026-09-30 | 2026-11-12 | 2026-11-13 04:48 | 13F-HR | 3,147 | 75,000.0 |"
            );
        // The Schedule 13G event filed on 2026-11-17 is not a 13F book.
        output.Should().NotContain("2026-11-17:").And.NotContain("0001037389-26-000099");
        output.Should().Contain("_Showing filings 1-4 (the last page)._");
    }

    [Fact]
    public async Task GetLatest13FFilings_QuarterFilter_KeepsOnlyThatQuartersFilingsAndPages()
    {
        await using var db = NewDb();
        await Seed(db);

        var output = await Tools(db).GetLatest13FFilings(reportDate: "2026-09-30", maxResults: 2);

        output.Should().Contain("Latest 13F filings loaded for 2026-09-30");
        output.Should().Contain("Loaded by SEC filing day: 2026-11-16: 2; 2026-11-12: 1.");
        // The amended second-quarter book filed in November belongs to another quarter.
        output.Should().NotContain("13F-HR/A");
        output.Should().Contain("_Showing filings 1-2 - pass offset=2 for older filings._");
    }

    [Fact]
    public async Task GetLatest13FFilings_ReportDateOffQuarterEnd_IsRefused()
    {
        await using var db = NewDb();

        var output = await Tools(db).GetLatest13FFilings(reportDate: "2026-09-15");

        output.Should().Contain("'2026-09-15' is not a calendar quarter end");
    }

    private static async Task Seed(EquiblesFinancialDbContext db)
    {
        var tiger = new InstitutionalHolder
        {
            Name = "TIGER GLOBAL MANAGEMENT LLC",
            Cik = "1167483",
        };
        var appaloosa = new InstitutionalHolder { Name = "APPALOOSA LP", Cik = "1656456" };
        var renaissance = new InstitutionalHolder
        {
            Name = "RENAISSANCE TECHNOLOGIES LLC",
            Cik = "1037389",
        };
        db.AddRange(tiger, appaloosa, renaissance);
        var loadedNextMorning = new DateTime(2026, 11, 17, 12, 42, 1, DateTimeKind.Utc);
        db.AddRange(
            Filing(
                tiger,
                "0000919574-26-008062",
                Q3,
                new(2026, 11, 16),
                45,
                20_173_101_159,
                loadedNextMorning
            ),
            Filing(
                appaloosa,
                "0001656456-26-000004",
                Q3,
                new(2026, 11, 16),
                24,
                6_000_000_000,
                loadedNextMorning.AddMinutes(-20)
            ),
            Filing(
                renaissance,
                "0001037389-26-000062",
                Q3,
                new(2026, 11, 12),
                3147,
                75_000_000_000,
                new DateTime(2026, 11, 13, 4, 48, 16, DateTimeKind.Utc)
            ),
            Filing(
                appaloosa,
                "0001656456-26-000003",
                Q2,
                new(2026, 11, 5),
                27,
                5_500_000_000,
                new DateTime(2026, 11, 6, 3, 0, 0, DateTimeKind.Utc),
                amendment: true
            ),
            Filing(
                renaissance,
                "0001037389-26-000099",
                Q3,
                new(2026, 11, 17),
                1,
                900_000_000,
                loadedNextMorning,
                type: FilingType.Schedule13G
            )
        );
        await db.SaveChangesAsync();
    }

    private static InstitutionalFiling Filing(
        InstitutionalHolder holder,
        string accession,
        DateOnly reportDate,
        DateOnly filed,
        int positions,
        long value,
        DateTime loadedAt,
        bool amendment = false,
        FilingType type = FilingType.Form13F
    ) =>
        new()
        {
            InstitutionalHolder = holder,
            AccessionNumber = accession,
            ReportDate = reportDate,
            FilingDate = filed,
            PositionCount = positions,
            TotalValue = value,
            IsAmendment = amendment,
            FilingType = type,
            CreationTime = loadedAt,
        };

    private static LatestFilingsTools Tools(EquiblesFinancialDbContext db) =>
        new(
            new InstitutionalHoldingRepository(db),
            new ErrorManager(new ErrorRepository(db)),
            NullLogger<LatestFilingsTools>.Instance
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
