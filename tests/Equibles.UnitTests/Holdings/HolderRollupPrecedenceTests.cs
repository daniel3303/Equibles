using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

// Which parent wins a holder's rollup: GLEIF ultimate parent, then GLEIF direct parent, then the
// 13F reporting parent, then the holder itself. The reporting-only cases (sole notice, conflict,
// cycle, four-parent depth) are in HolderLinkRuleTests.
public class HolderRollupPrecedenceTests
{
    private static readonly DateOnly Q2 = new(2026, 6, 30);

    private readonly List<LinkHolder> _holders = [];
    private readonly List<LinkNotice> _notices = [];
    private readonly List<HolderLinkEvidence> _sourced = [];

    [Fact]
    public void GleifUltimateParent_OutranksTheDirectParentAndTheReportingParent()
    {
        Holders("100", "200", "300", "400");
        ReportsThrough("100", "200");
        Gleif(HolderLinkKind.GleifDirectParent, "100", "300");
        Gleif(HolderLinkKind.GleifUltimateParent, "100", "400");

        var rollup = Rollup("100");

        rollup.UltimateCik.Should().Be("400");
        rollup.Basis.Should().Be(HolderRollupBasis.GleifUltimateParent);
        rollup.HasConflictingEvidence.Should().BeFalse();
        rollup.ReportingParentCik.Should().Be("200");
        rollup.ReportingTopCik.Should().Be("200");
    }

    [Fact]
    public void GleifDirectParent_OutranksTheReportingParentAndFollowsItsChain()
    {
        Holders("100", "200", "300", "400");
        ReportsThrough("100", "200");
        Gleif(HolderLinkKind.GleifDirectParent, "100", "300");
        Gleif(HolderLinkKind.GleifDirectParent, "300", "400");

        var rollup = Rollup("100");

        rollup.UltimateCik.Should().Be("400");
        rollup.Basis.Should().Be(HolderRollupBasis.GleifDirectParent);
        rollup.ReportingTopCik.Should().Be("200");
    }

    [Fact]
    public void ReportingParent_DecidesWithoutGleifEvidence()
    {
        Holders("100", "200");
        ReportsThrough("100", "200");

        var rollup = Rollup("100");

        rollup.UltimateCik.Should().Be("200");
        rollup.Basis.Should().Be(HolderRollupBasis.ReportingParent);
        rollup.ReportingTopCik.Should().Be("200");
    }

    [Fact]
    public void NoEvidence_RollsUpToItself()
    {
        Holders("100");

        var rollup = Rollup("100");

        rollup.UltimateCik.Should().Be("100");
        rollup.Basis.Should().Be(HolderRollupBasis.Self);
        rollup.ReportingParentCik.Should().BeNull();
        rollup.ReportingTopCik.Should().BeNull();
        rollup.HasConflictingEvidence.Should().BeFalse();
    }

    [Fact]
    public void TwoGleifUltimateParents_FlagAConflictAndNeverFallThroughToALowerTier()
    {
        // Two LEIs of one filer name different ultimate parents.
        Holders("100", "200", "300", "400", "500");
        ReportsThrough("100", "200");
        Gleif(HolderLinkKind.GleifDirectParent, "100", "300");
        Gleif(HolderLinkKind.GleifUltimateParent, "100", "400", childLei: Lei('A'));
        Gleif(HolderLinkKind.GleifUltimateParent, "100", "500", childLei: Lei('B'));

        var rollup = Rollup("100");

        rollup.UltimateCik.Should().Be("100");
        rollup.Basis.Should().Be(HolderRollupBasis.Self);
        rollup.HasConflictingEvidence.Should().BeTrue();
        // Containment is a 13F statement, which the GLEIF conflict leaves alone.
        rollup.ReportingParentCik.Should().Be("200");
        rollup.ReportingTopCik.Should().Be("200");
    }

    [Fact]
    public void TwoGleifDirectParents_FlagAConflictAndNeverFallThroughToTheReportingParent()
    {
        Holders("100", "200", "300", "400");
        ReportsThrough("100", "200");
        Gleif(HolderLinkKind.GleifDirectParent, "100", "300", childLei: Lei('A'));
        Gleif(HolderLinkKind.GleifDirectParent, "100", "400", childLei: Lei('B'));

        var rollup = Rollup("100");

        rollup.UltimateCik.Should().Be("100");
        rollup.Basis.Should().Be(HolderRollupBasis.Self);
        rollup.HasConflictingEvidence.Should().BeTrue();
    }

    [Fact]
    public void GleifParentWithoutACik_DoesNotDecide()
    {
        // BlackRock Fund Advisors' GLEIF ultimate parent is BlackRock, Inc., whose LEI no holder
        // profile or N-CEN adviser carries, so the 13F reporting parent decides.
        Holders("2012383");
        ReportsThrough("1006249", "2012383");
        Gleif(HolderLinkKind.GleifUltimateParent, "1006249", null);

        var rollup = Rollup("1006249");

        rollup.UltimateCik.Should().Be("2012383");
        rollup.Basis.Should().Be(HolderRollupBasis.ReportingParent);
        rollup.HasConflictingEvidence.Should().BeFalse();
    }

    [Fact]
    public void Chain_ContinuesFromTheParentsOwnRollup()
    {
        // Shaped on Invesco Senior Secured Management: GLEIF names an Invesco adviser that files
        // only a 13F notice, and that adviser's notice names Invesco Ltd. alone.
        Holders("870481", "914208");
        ReportsThrough("20430", "914208");
        Gleif(HolderLinkKind.GleifUltimateParent, "870481", "20430");

        var links = Derive();

        var rollup = Rollup(links, "870481");
        rollup.UltimateCik.Should().Be("914208");
        rollup.Basis.Should().Be(HolderRollupBasis.GleifUltimateParent);
        rollup.ReportingParentCik.Should().BeNull();
        rollup.ReportingTopCik.Should().BeNull();
        Rollup(links, "20430").UltimateCik.Should().Be("914208");
    }

    [Fact]
    public void ReportingTop_FollowsTheReportingChainWhicheverTierDecides()
    {
        Holders("100", "200", "300", "900");
        ReportsThrough("100", "200");
        ReportsThrough("200", "300");
        Gleif(HolderLinkKind.GleifUltimateParent, "100", "900");

        var rollup = Rollup("100");

        rollup.UltimateCik.Should().Be("900");
        rollup.ReportingParentCik.Should().Be("200");
        rollup.ReportingTopCik.Should().Be("300");
    }

    [Fact]
    public void CycleAcrossTiers_LeavesEachFilerOnItself()
    {
        Holders("100", "200");
        Gleif(HolderLinkKind.GleifDirectParent, "100", "200");
        ReportsThrough("200", "100");

        var links = Derive();

        foreach (var cik in new[] { "100", "200" })
        {
            var rollup = Rollup(links, cik);
            rollup.UltimateCik.Should().Be(cik);
            rollup.HasConflictingEvidence.Should().BeTrue();
        }
        Rollup(links, "200").ReportingTopCik.Should().Be("100");
    }

    private HolderLinkSet Derive() =>
        HolderLinkRule.Derive(
            _holders,
            new Dictionary<Guid, DateOnly>(),
            [],
            _notices,
            [],
            _sourced,
            []
        );

    private HolderRollup Rollup(string cik) => Rollup(Derive(), cik);

    private static HolderRollup Rollup(HolderLinkSet links, string cik) =>
        links.Rollups.Single(rollup => rollup.Cik == cik);

    private void Holders(params string[] ciks)
    {
        foreach (var cik in ciks)
            _holders.Add(new LinkHolder(Guid.NewGuid(), cik, null, null, null));
    }

    // The child's 13F notice names the parent alone, which confirms the reporting link.
    private void ReportsThrough(string childCik, string parentCik) =>
        _notices.Add(
            new LinkNotice(
                "n-" + childCik,
                childCik,
                null,
                null,
                Q2,
                Q2.AddDays(45),
                [new ManagerReference(parentCik, null, null, null)]
            )
        );

    private void Gleif(
        HolderLinkKind kind,
        string childCik,
        string parentCik,
        string childLei = null
    )
    {
        childLei ??= Lei('C');
        _sourced.Add(
            new HolderLinkEvidence
            {
                ChildCik = childCik,
                ParentCik = parentCik,
                ChildLei = childLei,
                ParentLei = Lei('P'),
                Kind = kind,
                SourceKey = childLei,
                AsOf = Q2,
            }
        );
    }

    private static string Lei(char mark) => new(mark, 20);
}
