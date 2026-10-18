using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// The succession refresh reads 13F rollups, included-manager lists and 13F notices from Postgres,
/// stores each predecessor-successor pair once, and drops a pair when the predecessor files again.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HolderSuccessionRefreshServiceTests : ParadeDbMcpTestBase
{
    private static readonly DateOnly Today = new(2026, 10, 17);

    public HolderSuccessionRefreshServiceTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Run_StoresSuccessorsTiedByNoticeIncludedManagersAndIdentityNumbers()
    {
        var group = Holder("0000102909");
        Rollup(group, "g-2025q3", "2025-09-30");
        Rollup(group, "g-2025q4", "2025-12-31");
        Included("g-2025q4", 1, cik: "0000933478");
        // The amendment's list is read with the original's.
        Rollup(group, "g-2025q4-a", "2025-12-31", filed: "2026-03-01");
        Included("g-2025q4-a", 1, fileNumber: "028-16887");
        var fiduciary = Holder("933478");
        Rollup(fiduciary, "f-2026q1", "2026-03-31");
        var advisers = Holder("947529", fileNumber: "028-16887");
        Rollup(advisers, "a-2026q1", "2026-03-31");
        var capital = Holder("2100119");
        Rollup(capital, "c-2026q1", "2026-03-31");
        Notice("0000102909-26-002714", "102909", "2026-06-30", "2100119");
        // An off-quarter 13F-typed rollup is never a first or last 13F quarter.
        Rollup(capital, "c-stray", "2026-01-31");
        Rollup(capital, "c-mid-quarter", "2026-03-15");
        var refiled = Holder("22", crd: "123456");
        Rollup(refiled, "r-2025q2", "2025-06-30");
        var old = Holder("21", crd: "000123456");
        Rollup(old, "o-2025q1", "2025-03-31");
        await DbContext.SaveChangesAsync();

        (await Sut().Run(Today, CancellationToken.None)).Should().Be(4);

        var stored = await Successions();
        stored
            .Where(succession => succession.PredecessorHolderId == group)
            .Select(succession => succession.SuccessorHolderId)
            .Should()
            .BeEquivalentTo([fiduciary, advisers, capital]);
        stored
            .Single(succession => succession.SuccessorHolderId == capital)
            .EvidenceAccession.Should()
            .Be("0000102909-26-002714");
        var byAdvisers = stored.Single(succession => succession.SuccessorHolderId == advisers);
        byAdvisers.Basis.Should().Be(HolderSuccessionBasis.IncludedInLastReport);
        byAdvisers.EvidenceAccession.Should().Be("g-2025q4-a");
        // The group's last quarter sums the original and its amendment; its successors' first
        // quarters sum to three filings.
        stored
            .Where(succession => succession.PredecessorHolderId == group)
            .Should()
            .OnlyContain(succession =>
                succession.PredecessorGroupValue == 2_000_000L
                && succession.SuccessorGroupValue == 3_000_000L
            );
        var byCrd = stored.Single(succession => succession.PredecessorHolderId == old);
        byCrd.SuccessorHolderId.Should().Be(refiled);
        byCrd.Basis.Should().Be(HolderSuccessionBasis.SameIdentityNumber);
    }

    [Fact]
    public async Task Run_PredecessorFilingAgain_DropsItsSuccessions()
    {
        var stopped = Holder("31", crd: "777");
        Rollup(stopped, "s-2025q4", "2025-12-31");
        var started = Holder("32", crd: "777");
        Rollup(started, "n-2026q1", "2026-03-31");
        await DbContext.SaveChangesAsync();
        (await Sut().Run(Today, CancellationToken.None)).Should().Be(1);

        Rollup(stopped, "s-2026q1", "2026-03-31");
        await DbContext.SaveChangesAsync();

        (await Sut().Run(Today, CancellationToken.None)).Should().Be(0);
        (await Successions()).Should().BeEmpty();
    }

    private Guid Holder(string cik, string fileNumber = null, string crd = null)
    {
        var holder = new InstitutionalHolder
        {
            Cik = cik,
            Name = "Filer " + cik,
            Form13FFileNumber = fileNumber,
            CrdNumber = crd,
        };
        DbContext.Add(holder);
        return holder.Id;
    }

    private void Rollup(Guid holder, string accession, string reportDate, string filed = null)
    {
        var report = DateOnly.Parse(reportDate);
        DbContext.Add(
            new InstitutionalFiling
            {
                AccessionNumber = accession,
                InstitutionalHolderId = holder,
                ReportDate = report,
                FilingDate = filed == null ? report.AddDays(40) : DateOnly.Parse(filed),
                FilingType = FilingType.Form13F,
                PositionCount = 1,
                TotalValue = 1_000_000L,
            }
        );
    }

    private void Included(
        string accession,
        int sequence,
        string cik = null,
        string fileNumber = null
    ) =>
        DbContext.Add(
            new FilingOtherManager
            {
                AccessionNumber = accession,
                Direction = OtherManagerDirection.IncludedInReport,
                SequenceNumber = sequence,
                Cik = cik,
                Form13FFileNumber = fileNumber,
            }
        );

    private void Notice(string accession, string filerCik, string reportDate, string reporterCik)
    {
        var notice = new Form13FNotice
        {
            AccessionNumber = accession,
            FilerCik = filerCik,
            ReportDate = DateOnly.Parse(reportDate),
            FilingDate = DateOnly.Parse(reportDate).AddDays(44),
            DataSetFileName = "01jun2026-31aug2026_form13f.zip",
        };
        notice.Reporters.Add(
            new Form13FNoticeReporter
            {
                Form13FNoticeId = notice.Id,
                Ordinal = 1,
                Cik = reporterCik,
            }
        );
        DbContext.Add(notice);
    }

    private async Task<List<HolderSuccession>> Successions()
    {
        await using var verify = Fixture.CreateDbContext();
        return await verify.Set<HolderSuccession>().ToListAsync();
    }

    private HolderSuccessionRefreshService Sut() =>
        new(Fixture.CreateDbContext(), NullLogger<HolderSuccessionRefreshService>());
}
