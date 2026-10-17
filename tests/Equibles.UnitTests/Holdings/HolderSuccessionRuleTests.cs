using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HolderSuccessionRuleTests
{
    private static readonly DateOnly Closed = new(2026, 6, 30);

    private readonly Dictionary<(Guid, DateOnly), SuccessionReport> _reports = [];
    private readonly List<SuccessionNotice> _notices = [];

    [Fact]
    public void FilerSplitIntoItsReporters_HasEachNewReporterAsASuccessor()
    {
        // Shaped on Vanguard Group: its last 13F included two managers that now file their own,
        // and its later notice names those and a new manager as reporting for it.
        var group = Filer("102909", "2013-06-30", "2025-12-31");
        var fiduciary = Filer("933478", "2026-03-31", "2026-06-30");
        var advisers = Filer("947529", "2026-03-31", "2026-06-30", fileNumber: "028-16887");
        var capital = Filer("2100119", "2026-03-31", "2026-06-30");
        var unrelated = Filer("2200000", "2026-03-31", "2026-06-30");
        // Reported for the group before the split, so it is not a successor.
        var personalized = Filer("1767306", "2020-03-31", "2026-06-30");
        Report(
            group,
            "2025-12-31",
            "g-last",
            Manager(cik: "0000933478"),
            Manager(fileNumber: "28-16887")
        );
        _notices.Add(
            Notice(
                "102909",
                "2026-06-30",
                "g-notice",
                Manager(cik: "2100119"),
                Manager(cik: "1767306"),
                Manager(cik: "933478")
            )
        );

        var successions = Derive(group, fiduciary, advisers, capital, unrelated, personalized);

        successions
            .Select(succession => succession.SuccessorHolderId)
            .Should()
            .BeEquivalentTo([fiduciary.HolderId, advisers.HolderId, capital.HolderId]);
        successions
            .Should()
            .OnlyContain(succession =>
                succession.PredecessorHolderId == group.HolderId
                && succession.PredecessorLastReportDate == new DateOnly(2025, 12, 31)
                && succession.SuccessorFirstReportDate == new DateOnly(2026, 3, 31)
                && succession.DerivationVersion == HolderSuccessionRule.Version
            );
        var bySuccessor = successions.ToDictionary(succession => succession.SuccessorHolderId);
        bySuccessor[fiduciary.HolderId]
            .Basis.Should()
            .Be(
                HolderSuccessionBasis.NoticeReportedBy | HolderSuccessionBasis.IncludedInLastReport
            );
        // The notice is the strongest evidence, so its accession is kept.
        bySuccessor[fiduciary.HolderId].EvidenceAccession.Should().Be("g-notice");
        bySuccessor[advisers.HolderId]
            .Basis.Should()
            .Be(HolderSuccessionBasis.IncludedInLastReport);
        bySuccessor[advisers.HolderId].EvidenceAccession.Should().Be("g-last");
        bySuccessor[capital.HolderId].Basis.Should().Be(HolderSuccessionBasis.NoticeReportedBy);
    }

    [Fact]
    public void ReRegisteredHoldingCompany_IsTiedByItsIncludedManagers()
    {
        // Shaped on BlackRock: the new CIK's first 13F includes every manager the old CIK's last did.
        var old = Filer("1364742", "2013-06-30", "2024-06-30");
        var renamed = Filer("2012383", "2024-09-30", "2026-06-30");
        var partial = Filer("2300000", "2024-09-30", "2026-06-30");
        Report(old, "2024-06-30", "o-last", Managers("028-04295", "028-13359", "028-12016"));
        Report(
            renamed,
            "2024-09-30",
            "n-first",
            Managers("028-04295", "028-13359", "028-12016", "028-03946")
        );
        Report(partial, "2024-09-30", "p-first", Managers("028-04295", "028-13359"));

        var succession = Derive(old, renamed, partial).Should().ContainSingle().Subject;

        succession.SuccessorHolderId.Should().Be(renamed.HolderId);
        succession.Basis.Should().Be(HolderSuccessionBasis.SameIncludedManagers);
        succession.EvidenceAccession.Should().Be("n-first");
    }

    [Fact]
    public void FewerSharedManagersThanTheMinimum_AreNotEnough()
    {
        var old = Filer("1", "2020-03-31", "2024-06-30");
        var renamed = Filer("2", "2024-09-30", "2026-06-30");
        Report(old, "2024-06-30", "o", Managers("028-1", "028-2"));
        Report(renamed, "2024-09-30", "n", Managers("028-1", "028-2", "028-3"));

        Derive(old, renamed).Should().BeEmpty();
    }

    [Fact]
    public void NewFilerIncludingStoppedFilers_SucceedsEachOfThem()
    {
        var first = Filer("11", "2020-03-31", "2025-06-30");
        var second = Filer("12", "2020-03-31", "2025-06-30", fileNumber: "028-777");
        var merged = Filer("13", "2025-09-30", "2026-06-30");
        Report(
            merged,
            "2025-09-30",
            "m-first",
            Manager(cik: "11"),
            Manager(fileNumber: "028-00777")
        );

        var successions = Derive(first, second, merged);

        successions
            .Select(succession => succession.PredecessorHolderId)
            .Should()
            .BeEquivalentTo([first.HolderId, second.HolderId]);
        successions
            .Should()
            .OnlyContain(succession =>
                succession.SuccessorHolderId == merged.HolderId
                && succession.Basis == HolderSuccessionBasis.SuccessorIncludesPredecessor
                && succession.EvidenceAccession == "m-first"
            );
    }

    [Fact]
    public void SameCrdUnderANewCik_IsASuccession()
    {
        var old = Filer("21", "2020-03-31", "2025-03-31", crd: "000123456");
        var refiled = Filer("22", "2025-06-30", "2026-06-30", crd: "123456");

        var succession = Derive(old, refiled).Should().ContainSingle().Subject;

        succession.Basis.Should().Be(HolderSuccessionBasis.SameIdentityNumber);
        succession.EvidenceAccession.Should().BeNull();
    }

    [Fact]
    public void PredecessorThatFilesAgain_HasNoSuccessor()
    {
        // It resumed after a gap, so its last 13F is not the quarter before the new filer's first.
        var resumed = Filer("31", "2020-03-31", "2026-03-31");
        var reporter = Filer("32", "2025-03-31", "2026-06-30");
        _notices.Add(Notice("31", "2025-03-31", "n", Manager(cik: "32")));

        Derive(resumed, reporter).Should().BeEmpty();
    }

    [Fact]
    public void SuccessorQuarterStillInItsFilingWindow_DecidesNothing()
    {
        var stopped = Filer("41", "2020-03-31", "2026-06-30", crd: "555");
        var started = Filer("42", "2026-09-30", "2026-09-30", crd: "555");

        Derive(stopped, started).Should().BeEmpty();
    }

    [Fact]
    public void NoticeForTheLastFiledQuarter_IsNotEvidence()
    {
        var stopped = Filer("51", "2020-03-31", "2025-12-31");
        var started = Filer("52", "2026-03-31", "2026-06-30");
        _notices.Add(Notice("51", "2025-12-31", "n", Manager(cik: "52")));

        Derive(stopped, started).Should().BeEmpty();
    }

    [Fact]
    public void IdentifiersMatchOnlyTheirOwnKind()
    {
        // A CIK and a CRD with the same digits name different things.
        var stopped = Filer("61", "2020-03-31", "2025-12-31");
        var started = Filer("62", "2026-03-31", "2026-06-30", crd: "7000");
        Report(stopped, "2025-12-31", "s", Manager(cik: "7000"));

        Derive(stopped, started).Should().BeEmpty();
    }

    private List<HolderSuccession> Derive(params SuccessionFiler[] filers) =>
        HolderSuccessionRule.Derive(filers, _reports, _notices, Closed);

    private void Report(
        SuccessionFiler filer,
        string reportDate,
        string accession,
        params ManagerReference[] included
    ) =>
        _reports[(filer.HolderId, DateOnly.Parse(reportDate))] = new SuccessionReport(
            accession,
            included
        );

    private static SuccessionFiler Filer(
        string cik,
        string first,
        string last,
        string fileNumber = null,
        string crd = null
    ) =>
        new(
            Guid.NewGuid(),
            cik,
            fileNumber,
            crd,
            null,
            DateOnly.Parse(first),
            DateOnly.Parse(last)
        );

    private static ManagerReference Manager(
        string cik = null,
        string fileNumber = null,
        string crd = null
    ) => new(cik, fileNumber, crd, null);

    private static ManagerReference[] Managers(params string[] fileNumbers) =>
        fileNumbers.Select(fileNumber => Manager(fileNumber: fileNumber)).ToArray();

    private static SuccessionNotice Notice(
        string filerCik,
        string reportDate,
        string accession,
        params ManagerReference[] reporters
    ) => new(filerCik, accession, DateOnly.Parse(reportDate), reporters);
}
