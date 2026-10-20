using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class FundSeriesHolderLinkTests
{
    private static readonly DateOnly Quarter = new(2026, 6, 30);
    private static readonly DateOnly Period = new(2025, 12, 31);

    private readonly List<LinkHolder> _holders = [];
    private readonly List<LinkManagerRow> _managers = [];
    private readonly List<LinkNotice> _notices = [];
    private readonly List<SeriesAdviserRow> _advisers = [];

    [Fact]
    public void SoleAdviserOnly()
    {
        var adviser = Holder("500", adviserCrd: 105958);
        Holder("600", adviserCrd: 104000);
        // One adviser beside a sub-adviser (sub-advisers are never loaded) links.
        Adviser("S000000001", "acc-1", 105958);
        Adviser("S000000002", "acc-2", 105958);
        Adviser("S000000002", "acc-2", 104000);
        Adviser("S000000003", "acc-3", 105958);
        Adviser("S000000003", "acc-3", null);
        // An older report naming two advisers no longer speaks for the series.
        Adviser("S000000004", "acc-4-old", 105958, period: Period.AddYears(-1));
        Adviser("S000000004", "acc-4-old", 104000, period: Period.AddYears(-1));
        Adviser("S000000004", "acc-4", 105958);

        var links = Derive().FundSeriesLinks;

        links.Select(link => link.SeriesId).Should().BeEquivalentTo(["S000000001", "S000000004"]);
        links
            .Should()
            .OnlyContain(link =>
                link.InstitutionalHolderId == adviser
                && link.AdviserCrd == 105958
                && link.Basis == FundSeriesHolderLinkBasis.AdviserFiler
                && link.NCenReportEndingPeriod == Period
                && link.RegistrantCik == "36405"
            );
    }

    [Fact]
    public void NoticeCrdNestsUnderReporter()
    {
        // Shaped on Fidelity: FMR Co files no 13F of its own, and its notice names FMR LLC alone.
        var fmr = Holder("315066");
        _notices.Add(
            new LinkNotice(
                "fmr-co-q2",
                "35368",
                "028-00450",
                "000108281",
                Quarter,
                Quarter.AddDays(45),
                [new ManagerReference("315066", null, null, null)]
            )
        );
        Adviser("S000000010", "acc-10", 108281);

        var link = Derive().FundSeriesLinks.Should().ContainSingle().Subject;

        link.InstitutionalHolderId.Should().Be(fmr);
        link.Basis.Should().Be(FundSeriesHolderLinkBasis.ReportingParent);
        link.AdviserCrd.Should().Be(108281);
    }

    [Fact]
    public void ProfileAdviserThatStoppedFiling_NestsUnderItsReporter()
    {
        // Shaped on BlackRock Fund Advisors: its Form ADV profile is on a 2016 13F filer, and its
        // CRD is filed only on BlackRock, Inc.'s included-manager line, beside its CIK.
        var blackRock = Holder("2012383");
        Holder("1006249", adviserCrd: 105247);
        _managers.Add(
            new LinkManagerRow(
                "br-q2",
                blackRock,
                Quarter,
                Quarter.AddDays(45),
                OtherManagerDirection.IncludedInReport,
                new ManagerReference("1006249", "028-13359", "000105247", null)
            )
        );
        _notices.Add(
            new LinkNotice(
                "bfa-q2",
                "1006249",
                "028-13359",
                null,
                Quarter,
                Quarter.AddDays(45),
                [new ManagerReference("2012383", null, null, null)]
            )
        );
        Adviser("S000004310", "acc-ishares", 105247);

        var link = Derive().FundSeriesLinks.Should().ContainSingle().Subject;

        link.InstitutionalHolderId.Should().Be(blackRock);
        link.Basis.Should().Be(FundSeriesHolderLinkBasis.ReportingParent);
    }

    [Fact]
    public void CrdOnSeveralProfilesOrNoFiler_DoesNotLink()
    {
        Holder("700", adviserCrd: 300);
        Holder("701", adviserCrd: 300);
        Adviser("S000000020", "acc-20", 300);
        // A CRD no filed record carries is never matched another way.
        Adviser("S000000021", "acc-21", 301);

        Derive().FundSeriesLinks.Should().BeEmpty();
    }

    private HolderLinkSet Derive() =>
        HolderLinkRule.Derive(_holders, _managers, _notices, [], [], _advisers);

    private Guid Holder(string cik, int? adviserCrd = null)
    {
        var id = Guid.NewGuid();
        _holders.Add(new LinkHolder(id, cik, null, null, adviserCrd));
        return id;
    }

    private void Adviser(string seriesId, string accession, int? crd, DateOnly? period = null) =>
        _advisers.Add(
            new SeriesAdviserRow(
                seriesId,
                "0000036405",
                period ?? Period,
                (period ?? Period).AddMonths(2),
                accession,
                crd
            )
        );
}
