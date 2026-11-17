using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using FluentAssertions;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// The latest-filings tool groups and bounds the filing rollup in SQL; this runs those queries
/// on Postgres, where a shape the in-memory provider accepts can fail to translate.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class LatestFilingsToolsTests : ParadeDbMcpTestBase
{
    public LatestFilingsToolsTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetLatest13FFilings_FilteredToAQuarterSinceADay_CountsAndListsOnPostgres()
    {
        var tiger = new InstitutionalHolder
        {
            Cik = "1167483",
            Name = "TIGER GLOBAL MANAGEMENT LLC",
        };
        var renaissance = new InstitutionalHolder
        {
            Cik = "1037389",
            Name = "RENAISSANCE TECHNOLOGIES LLC",
        };
        DbContext.AddRange(tiger, renaissance);
        DbContext.AddRange(
            Filing(tiger, "0000919574-26-008062", new(2026, 9, 30), new(2026, 11, 16)),
            Filing(renaissance, "0001037389-26-000062", new(2026, 9, 30), new(2026, 11, 12)),
            Filing(renaissance, "0001037389-26-000059", new(2026, 6, 30), new(2026, 8, 13)),
            Filing(
                renaissance,
                "0001037389-26-000099",
                new(2026, 9, 30),
                new(2026, 11, 17),
                FilingType.Schedule13G
            )
        );
        await DbContext.SaveChangesAsync();
        var tools = new LatestFilingsTools(
            new InstitutionalHoldingRepository(DbContext),
            ErrorManager,
            NullLogger<LatestFilingsTools>()
        );

        var output = await tools.GetLatest13FFilings("2026-09-30", "2026-11-01");

        output.Should().Contain("Loaded by SEC filing day: 2026-11-16: 1; 2026-11-12: 1.");
        output.Should().Contain("Newest load among these filing days: 2026-11-17 12:42 UTC.");
        output
            .Should()
            .Contain("| 1 | TIGER GLOBAL MANAGEMENT LLC | 1167483 | 2026-09-30 | 2026-11-16 |");
        output
            .Should()
            .Contain("| 2 | RENAISSANCE TECHNOLOGIES LLC | 1037389 | 2026-09-30 | 2026-11-12 |");
        output.Should().Contain("_Showing filings 1-2 (the last page)._");
    }

    private static InstitutionalFiling Filing(
        InstitutionalHolder holder,
        string accession,
        DateOnly reportDate,
        DateOnly filed,
        FilingType type = FilingType.Form13F
    ) =>
        new()
        {
            InstitutionalHolder = holder,
            AccessionNumber = accession,
            ReportDate = reportDate,
            FilingDate = filed,
            PositionCount = 10,
            TotalValue = 1_000_000_000,
            FilingType = type,
            CreationTime = new DateTime(2026, 11, 17, 12, 42, 1, DateTimeKind.Utc),
        };
}
