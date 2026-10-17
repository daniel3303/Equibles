using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Against Postgres: the rollup backfill removes a 13F-typed rollup only when its date is not a
/// calendar quarter end and nothing is stored under its accession.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FilingRollupTypeBackfillOffQuarterTests : ParadeDbMcpTestBase
{
    public FilingRollupTypeBackfillOffQuarterTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Backfill_RemovesOnlyThirteenFRollupsOffAQuarterEndWithNoHoldings()
    {
        var holder = new InstitutionalHolder { Cik = "1164688", Name = "Filer" };
        DbContext.Add(holder);
        DbContext.AddRange(
            Rollup(holder, "off-month", new DateOnly(2026, 4, 17), FilingType.Form13F, 1),
            Rollup(holder, "off-day", new DateOnly(2026, 6, 29), FilingType.Form13F, 1),
            Rollup(holder, "month-end", new DateOnly(2026, 4, 30), FilingType.Form13F, 1),
            // A 13F with no positions is filed as of a quarter end and has no holdings rows.
            Rollup(holder, "quarter-end", new DateOnly(2026, 6, 30), FilingType.Form13F, 0),
            Rollup(holder, "event-date", new DateOnly(2026, 4, 17), FilingType.Schedule13G, 1),
            // Mis-dated but still backed by holdings: the importer heals it, so it stays.
            Rollup(holder, "held", new DateOnly(2026, 4, 17), FilingType.Form13F, 1)
        );
        var issuer = new EquityIssuer { Name = "Issuer" };
        DbContext.Add(issuer);
        DbContext.Add(
            new InstitutionalHolding
            {
                EquityIssuerId = issuer.Id,
                InstitutionalHolderId = holder.Id,
                Cusip = "037833100",
                ReportDate = new DateOnly(2026, 4, 17),
                FilingDate = new DateOnly(2026, 4, 27),
                FilingType = FilingType.Form13F,
                ShareType = ShareType.Shares,
                Shares = 100,
                AccessionNumber = "held",
            }
        );
        await DbContext.SaveChangesAsync();

        (await Sut().Backfill(CancellationToken.None)).Should().Be(3);
        (await Sut().Backfill(CancellationToken.None)).Should().Be(0);

        await using var verify = Fixture.CreateDbContext();
        (await verify.Set<InstitutionalFiling>().Select(f => f.AccessionNumber).ToListAsync())
            .Should()
            .BeEquivalentTo(["quarter-end", "event-date", "held"]);
    }

    private static InstitutionalFiling Rollup(
        InstitutionalHolder holder,
        string accession,
        DateOnly reportDate,
        FilingType type,
        int positions
    ) =>
        new()
        {
            AccessionNumber = accession,
            InstitutionalHolderId = holder.Id,
            ReportDate = reportDate,
            FilingDate = reportDate.AddDays(10),
            FilingType = type,
            PositionCount = positions,
            TotalValue = positions * 1_000_000L,
        };

    private FilingRollupTypeBackfillService Sut() =>
        new(
            ServiceScopeSubstitute.Create(
                (typeof(EquiblesFinancialDbContext), Fixture.CreateDbContext())
            ),
            NullLogger<FilingRollupTypeBackfillService>()
        );
}
