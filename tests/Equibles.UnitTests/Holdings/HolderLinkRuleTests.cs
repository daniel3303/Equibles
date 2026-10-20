using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HolderLinkRuleTests
{
    private static readonly DateOnly Q1 = new(2026, 3, 31);
    private static readonly DateOnly Q2 = new(2026, 6, 30);

    private readonly List<LinkHolder> _holders = [];
    private readonly Dictionary<Guid, DateOnly> _own13F = [];
    private readonly List<LinkManagerRow> _managers = [];
    private readonly List<LinkNotice> _notices = [];
    private readonly List<(Guid, Guid)> _successions = [];
    private readonly List<HolderLinkEvidence> _sourced = [];

    [Fact]
    public void SoleReporterNotice_IsTheReportingParent()
    {
        // Shaped on BlackRock Fund Advisors: it files a notice naming BlackRock, Inc. alone, and
        // BlackRock, Inc. lists it among the managers its 13F includes.
        var parent = Holder("2012383", fileNumber: "028-24466");
        var child = Holder("1006249", fileNumber: "028-13359");
        Included(parent, Q2, "p-q2", Manager(cik: "0001006249"));
        Notice("1006249", Q2, "c-q2", [Manager(cik: "2012383")]);

        var links = Derive();

        var rollup = Rollup(links, "1006249");
        rollup.ReportingParentCik.Should().Be("2012383");
        rollup.UltimateCik.Should().Be("2012383");
        rollup.UltimateHolderId.Should().Be(parent);
        rollup.Basis.Should().Be(HolderRollupBasis.ReportingParent);
        rollup.HasConflictingEvidence.Should().BeFalse();
        links.Evidence.Should().OnlyContain(row => row.Confirmed);
        links
            .Evidence.Should()
            .OnlyContain(row => row.ChildHolderId == child && row.ParentHolderId == parent);
        Rollup(links, "2012383").Basis.Should().Be(HolderRollupBasis.Self);
        links
            .Evidence.Select(row => (row.ChildCik, row.ParentCik, row.Kind))
            .Should()
            .BeEquivalentTo([
                ("1006249", "2012383", HolderLinkKind.IncludedInParentReport),
                ("1006249", "2012383", HolderLinkKind.NoticeReportedBy),
            ]);
    }

    [Fact]
    public void NameOnlyNeverLinks()
    {
        var parent = Holder("2012383");
        Holder("1086363");
        // Neither row carries an identifier, however closely the names match the filers'.
        Included(parent, Q2, "p-q2", NameOnly("BLACKROCK FINANCIAL MANAGEMENT INC/DE"));
        Notice("1086363", Q2, "c-q2", [NameOnly("BlackRock, Inc.")]);

        var links = Derive();

        links.Evidence.Should().BeEmpty();
        links.Rollups.Should().OnlyContain(rollup => rollup.Basis == HolderRollupBasis.Self);
        links.Rollups.Select(rollup => rollup.Cik).Should().BeEquivalentTo(["2012383", "1086363"]);
    }

    [Fact]
    public void SuccessionPairNeverLinks()
    {
        // The old BlackRock CIK's notice names its successor, which is a CIK change, not a parent.
        var old = Holder("1364742");
        var renamed = Holder("2012383");
        _successions.Add((old, renamed));
        Notice("1364742", Q2, "o-q2", [Manager(cik: "2012383")]);
        Included(renamed, Q2, "r-q2", Manager(cik: "1364742"));

        var links = Derive();

        links.Evidence.Should().BeEmpty();
        Rollup(links, "1364742").UltimateCik.Should().Be("1364742");
        Rollup(links, "1364742").ReportingParentCik.Should().BeNull();
    }

    [Fact]
    public void TwoParentsFlagConflict()
    {
        var first = Holder("100");
        var second = Holder("200");
        Holder("300");
        Included(first, Q2, "a-q2", Manager(cik: "300"));
        Included(second, Q2, "b-q2", Manager(cik: "300"));
        // The child's notice names both, so both included edges are confirmed.
        Notice("300", Q2, "c-q2", [Manager(cik: "100"), Manager(cik: "200")]);

        var rollup = Rollup(Derive(), "300");

        rollup.HasConflictingEvidence.Should().BeTrue();
        rollup.UltimateCik.Should().Be("300");
        rollup.ReportingParentCik.Should().BeNull();
        rollup.Basis.Should().Be(HolderRollupBasis.Self);
    }

    [Fact]
    public void CycleTerminates()
    {
        Holder("100");
        Holder("200");
        Holder("300");
        Notice("100", Q2, "a-q2", [Manager(cik: "200")]);
        Notice("200", Q2, "b-q2", [Manager(cik: "100")]);
        Notice("300", Q2, "c-q2", [Manager(cik: "100")]);

        var links = Derive();

        foreach (var cik in new[] { "100", "200", "300" })
        {
            var rollup = Rollup(links, cik);
            rollup.UltimateCik.Should().Be(cik);
            rollup.HasConflictingEvidence.Should().BeTrue();
        }
        Rollup(links, "300").ReportingParentCik.Should().Be("100");
    }

    [Fact]
    public void ChainFlattensToItsTopAndStopsAfterFourParents()
    {
        foreach (var cik in new[] { "1", "2", "3", "4", "5", "6" })
            Holder(cik);
        Notice("1", Q2, "n1", [Manager(cik: "2")]);
        Notice("2", Q2, "n2", [Manager(cik: "3")]);
        Notice("3", Q2, "n3", [Manager(cik: "4")]);
        Notice("4", Q2, "n4", [Manager(cik: "5")]);
        Notice("5", Q2, "n5", [Manager(cik: "6")]);

        var links = Derive();

        Rollup(links, "3").UltimateCik.Should().Be("6");
        Rollup(links, "1").UltimateCik.Should().Be("5");
        Rollup(links, "1").ReportingParentCik.Should().Be("2");
    }

    [Fact]
    public void IncludedEdgeAlone_IsNotAParent()
    {
        // Shaped on Cary Street Partners, whose 13F lists BlackRock, Inc. as both included and
        // reporting for it: BlackRock's own cover confirms neither direction.
        var filer = Holder("1766904");
        var blackRock = Holder("2012383");
        Included(filer, Q2, "f-q2", Manager(cik: "2012383"));
        Cover(filer, Q2, "f-q2", Manager(cik: "2012383"));
        Cover(blackRock, Q2, "b-q2", Manager(crd: "000109391"));

        var links = Derive();

        Rollup(links, "1766904").ReportingParentCik.Should().BeNull();
        Rollup(links, "2012383").ReportingParentCik.Should().BeNull();
        links
            .Evidence.Select(row => (row.ChildCik, row.ParentCik, row.Kind))
            .Should()
            .BeEquivalentTo([
                ("2012383", "1766904", HolderLinkKind.IncludedInParentReport),
                ("1766904", "2012383", HolderLinkKind.ChildCoverReportsBy),
            ]);
        links.Evidence.Should().OnlyContain(row => !row.Confirmed);
    }

    [Fact]
    public void ListingAnotherFilersNumber_IsNeverConfirmed()
    {
        // Shaped on Russell, whose 13F includes "BlackRock Financial Management" under BlackRock,
        // Inc.'s own 13F file number; BlackRock's cover names Russell only by name.
        var russell = Holder("1692234");
        var blackRock = Holder("2012383", fileNumber: "028-24466");
        Included(russell, Q2, "r-q2", Manager(fileNumber: "028-24466"));
        Cover(blackRock, Q2, "b-q2", NameOnly("RUSSELL FRANK CO"));

        var links = Derive();

        var row = links.Evidence.Should().ContainSingle().Subject;
        (row.ChildCik, row.ParentCik, row.Confirmed).Should().Be(("2012383", "1692234", false));
        Rollup(links, "2012383").ReportingParentCik.Should().BeNull();
    }

    [Fact]
    public void EveryQuarterConfirmsOnItsOwn()
    {
        Holder("100");
        Holder("200");
        Holder("300");
        Notice("300", Q1, "c-q1", [Manager(cik: "100")]);
        Notice("300", Q2, "c-q2", [Manager(cik: "200")]);

        var links = Derive();

        // The older quarter stays contained in its own parent; the newest decides the parent.
        links.Evidence.Should().OnlyContain(row => row.Confirmed);
        Rollup(links, "300").ReportingParentCik.Should().Be("200");
    }

    [Fact]
    public void IncludedEdgeConfirmedByTheChildsCover_IsAParent()
    {
        var parent = Holder("100", fileNumber: "28-451");
        var child = Holder("200");
        Included(parent, Q2, "p-q2", Manager(cik: "200"));
        Cover(child, Q2, "c-q2", Manager(fileNumber: "028-00451"));

        var rollup = Rollup(Derive(), "200");

        rollup.ReportingParentCik.Should().Be("100");
        rollup.Basis.Should().Be(HolderRollupBasis.ReportingParent);
    }

    [Fact]
    public void IncludedEdgeConfirmedByTheChildsCover_ConfirmsBothRows()
    {
        var parent = Holder("100");
        var child = Holder("200");
        Included(parent, Q2, "p-q2", Manager(cik: "200"));
        Cover(child, Q2, "c-q2", Manager(cik: "100"));
        // A cover naming the parent another quarter confirms nothing here.
        Cover(child, Q1, "c-q1", Manager(cik: "100"));

        var links = Derive();

        links
            .Evidence.Where(row => row.Confirmed)
            .Select(row => row.SourceKey)
            .Should()
            .BeEquivalentTo(["p-q2", "c-q2"]);
    }

    [Fact]
    public void NoticeNamingAnUnidentifiedSecondReporter_IsNotSole()
    {
        Holder("100");
        Holder("300");
        Notice("300", Q2, "c-q2", [Manager(cik: "100"), NameOnly("RUSSELL FRANK CO")]);

        var links = Derive();

        Rollup(links, "300").ReportingParentCik.Should().BeNull();
        var row = links.Evidence.Should().ContainSingle().Subject;
        row.ParentCik.Should().Be("100");
        row.Confirmed.Should().BeFalse();
    }

    [Fact]
    public void NewestOwnNoticeDecides()
    {
        Holder("1364742");
        Holder("2012383");
        Holder("1006249");
        Notice("1006249", Q1, "q1", [Manager(cik: "1364742")]);
        Notice("1006249", Q2, "q2", [Manager(cik: "2012383")]);

        var rollup = Rollup(Derive(), "1006249");

        rollup.ReportingParentCik.Should().Be("2012383");
        rollup.HasConflictingEvidence.Should().BeFalse();
    }

    [Fact]
    public void SpunOutFiler_NewerOwn13F_RollsToItself()
    {
        Holder("100");
        var child = Holder("200");
        Notice("200", Q1, "c-q1", [Manager(cik: "100")]);
        // Its next quarter is a full 13F report of its own that names no other manager.
        Filed13F(child, Q2);

        var rollup = Rollup(Derive(), "200");

        rollup.ReportingParentCik.Should().BeNull();
        rollup.UltimateCik.Should().Be("200");
        rollup.Basis.Should().Be(HolderRollupBasis.Self);
        rollup.HasConflictingEvidence.Should().BeFalse();
    }

    [Fact]
    public void AmendedNotice_ReplacesTheOriginal()
    {
        Holder("100");
        Holder("200");
        Holder("300");
        Notice("300", Q2, "original", [Manager(cik: "100")]);
        Notice(
            "300",
            Q2,
            "amendment",
            [Manager(cik: "100"), Manager(cik: "200")],
            filedDaysAfter: 60
        );

        var links = Derive();

        Rollup(links, "300").ReportingParentCik.Should().BeNull();
        links.Evidence.Should().OnlyContain(row => row.SourceKey == "amendment");
    }

    [Fact]
    public void ManagerResolvesByFileNumberBeforeCrd_AndAmbiguousNumbersStayUnresolved()
    {
        var parent = Holder("100", fileNumber: "028-00100", crd: "555");
        Holder("200", crd: "555");
        Holder("300", fileNumber: "028-00300", crd: "777");
        // The file number names 300 although the CRD on the same line is another filer's.
        Included(parent, Q2, "p-q2", Manager(fileNumber: "28-300", crd: "999"));
        // CRD 555 is claimed by two filers, so the line resolves to neither.
        Included(parent, Q2, "p-q2", Manager(crd: "000555"));
        // A number only another filer lists beside a CIK still resolves.
        Included(parent, Q2, "p-q2", Manager(cik: "400", fileNumber: "028-00400"));
        Included(parent, Q2, "p-q2b", Manager(fileNumber: "028-00400"));

        var links = Derive();

        links
            .Evidence.Select(row => row.ChildCik)
            .Distinct()
            .Should()
            .BeEquivalentTo(["300", "400"]);
    }

    [Fact]
    public void GleifUltimateParentOutranksTheReportingChain()
    {
        Holder("100");
        Holder("200");
        Holder("300");
        Notice("300", Q2, "c-q2", [Manager(cik: "100")]);
        _sourced.Add(
            new HolderLinkEvidence
            {
                ChildCik = "0000000300",
                ParentCik = "200",
                ChildLei = "LEICHILD000000000000",
                ParentLei = "LEIPARENT00000000000",
                Kind = HolderLinkKind.GleifUltimateParent,
                SourceKey = "LEICHILD000000000000",
                AsOf = Q2,
            }
        );
        // A source cannot add 13F evidence.
        _sourced.Add(
            new HolderLinkEvidence
            {
                ChildCik = "300",
                ParentCik = "100",
                Kind = HolderLinkKind.IncludedInParentReport,
                SourceKey = "x",
                AsOf = Q2,
            }
        );

        var links = Derive();

        var rollup = Rollup(links, "300");
        rollup.UltimateCik.Should().Be("200");
        rollup.Basis.Should().Be(HolderRollupBasis.GleifUltimateParent);
        rollup.ReportingParentCik.Should().Be("100");
        links.Evidence.Should().NotContain(row => row.SourceKey == "x");
    }

    private HolderLinkSet Derive() =>
        HolderLinkRule.Derive(_holders, _own13F, _managers, _notices, _successions, _sourced, []);

    private static HolderRollup Rollup(HolderLinkSet links, string cik) =>
        links.Rollups.Single(rollup => rollup.Cik == cik);

    private Guid Holder(string cik, string fileNumber = null, string crd = null)
    {
        var id = Guid.NewGuid();
        _holders.Add(new LinkHolder(id, cik, fileNumber, crd, null));
        return id;
    }

    // Either list comes from a 13F report the filer filed for that quarter.
    private void Included(
        Guid filer,
        DateOnly quarter,
        string accession,
        ManagerReference manager
    ) => Listed(filer, quarter, accession, OtherManagerDirection.IncludedInReport, manager);

    private void Cover(Guid filer, DateOnly quarter, string accession, ManagerReference manager) =>
        Listed(filer, quarter, accession, OtherManagerDirection.ReportsForFiler, manager);

    private void Listed(
        Guid filer,
        DateOnly quarter,
        string accession,
        OtherManagerDirection direction,
        ManagerReference manager
    )
    {
        Filed13F(filer, quarter);
        _managers.Add(
            new LinkManagerRow(accession, filer, quarter, quarter.AddDays(45), direction, manager)
        );
    }

    private void Filed13F(Guid filer, DateOnly quarter)
    {
        if (!_own13F.TryGetValue(filer, out var newest) || quarter > newest)
            _own13F[filer] = quarter;
    }

    private void Notice(
        string filerCik,
        DateOnly quarter,
        string accession,
        ManagerReference[] reporters,
        int filedDaysAfter = 45
    ) =>
        _notices.Add(
            new LinkNotice(
                accession,
                filerCik,
                null,
                null,
                quarter,
                quarter.AddDays(filedDaysAfter),
                reporters
            )
        );

    private static ManagerReference Manager(
        string cik = null,
        string fileNumber = null,
        string crd = null
    ) => new(cik, fileNumber, crd, null);

    // A manager listed by name with every identifier left blank.
    private static ManagerReference NameOnly(string name) => new(null, null, null, null);
}
