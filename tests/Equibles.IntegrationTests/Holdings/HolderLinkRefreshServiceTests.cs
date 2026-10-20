using Equibles.Holdings.BusinessLogic.Links;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// The link refresh reads 13F other-manager lists, notices, successions and N-CEN advisers from
/// Postgres and replaces the evidence, rollups and fund-series links together on each run.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HolderLinkRefreshServiceTests : ParadeDbMcpTestBase
{
    // The newest closed quarter is 2026-06-30, so the evidence window starts at 2024-09-30.
    private static readonly DateOnly Today = new(2026, 10, 20);
    private static readonly DateOnly Q2 = new(2026, 6, 30);

    public HolderLinkRefreshServiceTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Run_RollsSubsidiariesAndTheirFundsUpToTheReportingParent()
    {
        var blackRock = Holder("2012383", "028-24466");
        var bfa = Holder("1006249", "028-13359");
        Profile(bfa, 105247);
        var fmr = Holder("315066", "028-00451");
        Rollup(blackRock, "br-q2", Q2);
        Included("br-q2", 21, cik: "1006249", fileNumber: "028-13359", crd: "000105247");
        // An included manager the child never confirms stays unparented.
        Included("br-q2", 22, cik: "913414");
        Rollup(fmr, "fmr-q2", Q2);
        Included("fmr-q2", 1, cik: "35368", fileNumber: "028-00450", crd: "000108281");
        Notice("bfa-q2", "1006249", Q2, crd: null, reporters: ["2012383"]);
        Notice("fmrco-q2", "35368", Q2, crd: "000108281", reporters: ["315066"]);
        // Outside the window: a quarter that is still open, and one before the eighth quarter.
        Notice("fmrco-open", "35368", new DateOnly(2026, 9, 30), crd: null, reporters: ["2012383"]);
        Notice("bfa-old", "1006249", new DateOnly(2024, 6, 30), crd: null, reporters: ["1364742"]);
        AddFundReport("S000004310", "36405", 105247);
        AddFundReport("S000006037", "35315", 108281);
        await DbContext.SaveChangesAsync();

        (await Sut().Run(Today, CancellationToken.None)).Should().Be(5);

        await using var verify = Fixture.CreateDbContext();
        var rollups = await verify.Set<HolderRollup>().ToDictionaryAsync(rollup => rollup.Cik);
        rollups["1006249"].UltimateCik.Should().Be("2012383");
        rollups["1006249"].UltimateHolderId.Should().Be(blackRock.Id);
        rollups["1006249"].Basis.Should().Be(HolderRollupBasis.ReportingParent);
        rollups["35368"].UltimateCik.Should().Be("315066");
        rollups["35368"].ReportingParentCik.Should().Be("315066");
        rollups["913414"].Basis.Should().Be(HolderRollupBasis.Self);
        rollups["2012383"].UltimateCik.Should().Be("2012383");
        rollups.Should().NotContainKey("1364742");
        rollups
            .Values.Should()
            .OnlyContain(rollup => rollup.DerivationVersion == HolderLinkRule.Version);

        var funds = await verify
            .Set<FundSeriesHolderLink>()
            .ToDictionaryAsync(link => link.SeriesId);
        funds["S000004310"].InstitutionalHolderId.Should().Be(blackRock.Id);
        funds["S000006037"].InstitutionalHolderId.Should().Be(fmr.Id);
        funds
            .Values.Should()
            .OnlyContain(link => link.Basis == FundSeriesHolderLinkBasis.ReportingParent);

        var reader = new HolderRollupReader(new InstitutionalHolderRepository(verify));
        (await reader.GetRollups(["0001006249", "999"])).Keys.Should().BeEquivalentTo(["1006249"]);
        (await reader.GetContainedChildren(blackRock, Q2))
            .Select(evidence => (evidence.ChildCik, evidence.Kind))
            .Should()
            .BeEquivalentTo([
                ("1006249", HolderLinkKind.IncludedInParentReport),
                ("1006249", HolderLinkKind.NoticeReportedBy),
                ("913414", HolderLinkKind.IncludedInParentReport),
            ]);
        // Evidence holds for its own quarter and the next quarter end, never later.
        (await reader.GetContainedChildren(blackRock, new DateOnly(2026, 9, 30)))
            .Should()
            .HaveCount(3);
        (await reader.GetContainedChildren(blackRock, new DateOnly(2026, 12, 31)))
            .Should()
            .BeEmpty();
        (await reader.GetFundSeriesLinks(["S000004310", "S000000000"]))
            .Keys.Should()
            .BeEquivalentTo(["S000004310"]);
    }

    [Fact]
    public async Task Run_ReplacesTheStoredSetAndKeepsItWhenASourceFails()
    {
        var parent = Holder("100", null);
        Holder("200", null);
        Rollup(parent, "p-q2", Q2);
        Included("p-q2", 1, cik: "200");
        Notice("c-q2", "200", Q2, crd: null, reporters: ["100"]);
        await DbContext.SaveChangesAsync();
        (await Sut().Run(Today, CancellationToken.None)).Should().Be(2);

        // The child's amended notice names another manager alone, which no longer confirms the
        // parent's included edge.
        Notice("c-q2-a", "200", Q2, crd: null, reporters: ["300"], filedDaysAfter: 60);
        await DbContext.SaveChangesAsync();

        var failing = () => Sut(new FailingSource()).Run(Today, CancellationToken.None);
        await failing.Should().ThrowAsync<InvalidOperationException>();
        (await RollupOf("200")).ReportingParentCik.Should().Be("100");

        (await Sut().Run(Today, CancellationToken.None)).Should().Be(2);
        (await RollupOf("200")).ReportingParentCik.Should().Be("300");
        await using var verify = Fixture.CreateDbContext();
        (
            await verify
                .Set<HolderLinkEvidence>()
                .Select(row => row.SourceKey)
                .Distinct()
                .ToListAsync()
        )
            .Should()
            .BeEquivalentTo(["p-q2", "c-q2-a"]);
    }

    private async Task<HolderRollup> RollupOf(string cik)
    {
        await using var verify = Fixture.CreateDbContext();
        return await verify.Set<HolderRollup>().SingleAsync(rollup => rollup.Cik == cik);
    }

    private InstitutionalHolder Holder(string cik, string fileNumber)
    {
        var holder = new InstitutionalHolder
        {
            Cik = cik,
            Name = "Filer " + cik,
            Form13FFileNumber = fileNumber,
        };
        DbContext.Add(holder);
        return holder;
    }

    private void Profile(InstitutionalHolder holder, int adviserCrd) =>
        DbContext.Add(
            new InstitutionalHolderProfile
            {
                InstitutionalHolderId = holder.Id,
                EntityType = HolderEntityType.RegisteredAdviser,
                AdviserCrd = adviserCrd,
                DerivationVersion = 1,
            }
        );

    private void Rollup(InstitutionalHolder holder, string accession, DateOnly reportDate) =>
        DbContext.Add(
            new InstitutionalFiling
            {
                AccessionNumber = accession,
                InstitutionalHolderId = holder.Id,
                ReportDate = reportDate,
                FilingDate = reportDate.AddDays(45),
                FilingType = FilingType.Form13F,
                PositionCount = 1,
                TotalValue = 1_000_000L,
            }
        );

    private void Included(
        string accession,
        int sequence,
        string cik = null,
        string fileNumber = null,
        string crd = null
    ) =>
        DbContext.Add(
            new FilingOtherManager
            {
                AccessionNumber = accession,
                Direction = OtherManagerDirection.IncludedInReport,
                SequenceNumber = sequence,
                Cik = cik,
                Form13FFileNumber = fileNumber,
                CrdNumber = crd,
            }
        );

    private void Notice(
        string accession,
        string filerCik,
        DateOnly reportDate,
        string crd,
        string[] reporters,
        int filedDaysAfter = 45
    )
    {
        var notice = new Form13FNotice
        {
            AccessionNumber = accession,
            FilerCik = filerCik,
            CrdNumber = crd,
            ReportDate = reportDate,
            FilingDate = reportDate.AddDays(filedDaysAfter),
            DataSetFileName = "01jun2026-31aug2026_form13f.zip",
        };
        for (var ordinal = 0; ordinal < reporters.Length; ordinal++)
            notice.Reporters.Add(
                new Form13FNoticeReporter
                {
                    Form13FNoticeId = notice.Id,
                    Ordinal = ordinal + 1,
                    Cik = reporters[ordinal],
                }
            );
        DbContext.Add(notice);
    }

    private void AddFundReport(string seriesId, string registrantCik, int crd)
    {
        var period = new DateOnly(2025, 12, 31);
        var accession = "ncen-" + seriesId;
        var report = new NCenRegistrantReport
        {
            AccessionNumber = accession,
            RegistrantCik = registrantCik,
            RegistrantName = "Registrant " + registrantCik,
            InvestmentCompanyType = "N-1A",
            FilingDate = period.AddMonths(2),
            ReportEndingPeriod = period,
        };
        var fund = new NCenFundReport
        {
            RegistrantReportId = report.Id,
            FundId = accession + "-F1",
            SeriesId = seriesId,
            FundName = "Fund " + seriesId,
            RegistrantCik = registrantCik,
            AccessionNumber = accession,
            FilingDate = report.FilingDate,
            ReportEndingPeriod = period,
            FundTypes = NCenFundTypes.IndexFund,
        };
        fund.Advisers.Add(
            new NCenFundAdviser
            {
                FundReportId = fund.Id,
                Role = NCenAdviserRole.InvestmentAdviser,
                Name = "Adviser " + crd,
                Crd = crd,
            }
        );
        DbContext.AddRange(report, fund);
    }

    private HolderLinkRefreshService Sut(params IHolderLinkEvidenceSource[] sources) =>
        new(Fixture.CreateDbContext(), sources, NullLogger<HolderLinkRefreshService>());

    private sealed class FailingSource : IHolderLinkEvidenceSource
    {
        public Task<IReadOnlyList<HolderLinkEvidence>> Collect(
            DateOnly today,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException("GLEIF outage");
    }
}
