using Equibles.Sec.FinancialFacts.Mcp.Tools;
using FluentAssertions;

namespace Equibles.UnitTests.Sec;

// An issuer can tag two overlapping disaggregation schemes on the SAME geographical axis in
// one filing — e.g. an ASC 606 regional partition (Americas/Asia Pacific/EMEA) AND an
// enterprise-wide by-country partition (US/China/Singapore/Taiwan/All Other). Both partitions
// independently sum to consolidated total revenue, so the axis concatenates them and shows
// ~2x actual revenue (#3897). The fix is pure arithmetic: when the members of one period
// partition cleanly into 2+ disjoint subsets that EACH reconcile to the consolidated total,
// keep only the most granular subset (the most informative correct view).
public class RevenueBreakdownToolsCollapseOverlappingSchemesTests
{
    private const string Geo = "srt:StatementGeographicalAxis";
    private static readonly DateOnly Fy2025 = new(2025, 11, 2);
    private static readonly DateOnly Filed = new(2025, 12, 19);

    private static Dictionary<(DateOnly, string), IReadOnlyList<decimal>> Totals(
        params decimal[] usdTotals
    ) => new() { [(Fy2025, "USD")] = usdTotals };

    // AVGO FY2025: a 3-member region scheme and a 5-member country scheme, each summing to the
    // consolidated total of 63,887 in the SAME filing. The axis must collapse to ONE scheme —
    // the more granular 5-member country partition — reconciling to total, NOT 2x.
    [Fact]
    public void BuildAxisSeries_TwoOverlappingSchemesEachSummingToTotal_KeepsMostGranularScheme()
    {
        var rows = new List<RevenueBreakdownTools.DimensionalRevenueRow>
        {
            // Region scheme — 3 members, sums to 63,887.
            new(Geo, "avgo:AmericasMember", Fy2025, 18939m, "USD", Filed),
            new(Geo, "srt:AsiaPacificMember", Fy2025, 35896m, "USD", Filed),
            new(Geo, "us-gaap:EMEAMember", Fy2025, 9052m, "USD", Filed),
            // Country scheme — 5 members, also sums to 63,887.
            new(Geo, "country:US", Fy2025, 16506m, "USD", Filed),
            new(Geo, "country:CN", Fy2025, 11155m, "USD", Filed),
            new(Geo, "country:SG", Fy2025, 10796m, "USD", Filed),
            new(Geo, "country:TW", Fy2025, 6451m, "USD", Filed),
            new(Geo, "avgo:AllOtherMember", Fy2025, 18979m, "USD", Filed),
        };

        var (unit, periodEnds, members) = RevenueBreakdownTools.BuildAxisSeries(
            rows,
            [Geo],
            maxYears: 8,
            Totals(63887m)
        );

        unit.Should().Be("USD");
        periodEnds.Should().Equal(Fy2025);
        members
            .Should()
            .HaveCount(5, "the more granular by-country scheme is kept, the region scheme dropped");
        members
            .Sum(m => m.Values[0] ?? 0m)
            .Should()
            .Be(63887m, "the surviving scheme reconciles exactly to consolidated total revenue");
        members
            .Select(m => m.Label)
            .Should()
            .NotContain("Americas", "the overlapping region scheme is removed, not concatenated");
    }

    // CAT FY2025: a 4-member region scheme and a 2-member US/non-US scheme, each summing to the
    // consolidated total of 67,589. Keep the more granular 4-member region scheme.
    [Fact]
    public void BuildAxisSeries_RegionAndUsNonUsSchemes_KeepsFourMemberRegionScheme()
    {
        var rows = new List<RevenueBreakdownTools.DimensionalRevenueRow>
        {
            // Region scheme — 4 members summing to 67,589.
            new(Geo, "cat:NorthAmericaMember", Fy2025, 38000m, "USD", Filed),
            new(Geo, "us-gaap:EMEAMember", Fy2025, 14000m, "USD", Filed),
            new(Geo, "srt:AsiaPacificMember", Fy2025, 10589m, "USD", Filed),
            new(Geo, "cat:LatinAmericaMember", Fy2025, 5000m, "USD", Filed),
            // US / non-US scheme — 2 members also summing to 67,589.
            new(Geo, "country:US", Fy2025, 34000m, "USD", Filed),
            new(Geo, "cat:NonUsMember", Fy2025, 33589m, "USD", Filed),
        };

        var (_, _, members) = RevenueBreakdownTools.BuildAxisSeries(
            rows,
            [Geo],
            maxYears: 8,
            Totals(67589m)
        );

        members.Should().HaveCount(4, "the more granular 4-member region scheme is kept");
        members.Sum(m => m.Values[0] ?? 0m).Should().Be(67589m);
        members
            .Select(m => m.Label)
            .Should()
            .NotContain("Non Us", "the coarser US/non-US scheme is dropped");
    }

    // A single clean partition (one scheme = total) must be left fully intact — the collapse
    // only fires when 2+ disjoint full-total subsets exist.
    [Fact]
    public void BuildAxisSeries_SingleSchemeSummingToTotal_LeftIntact()
    {
        var rows = new List<RevenueBreakdownTools.DimensionalRevenueRow>
        {
            new(Geo, "country:US", Fy2025, 40m, "USD", Filed),
            new(Geo, "country:CN", Fy2025, 35m, "USD", Filed),
            new(Geo, "country:SG", Fy2025, 25m, "USD", Filed),
        };

        var (_, _, members) = RevenueBreakdownTools.BuildAxisSeries(
            rows,
            [Geo],
            maxYears: 8,
            Totals(100m)
        );

        members.Should().HaveCount(3, "a single full-total scheme is not a multi-scheme overlap");
        members.Sum(m => m.Values[0] ?? 0m).Should().Be(100m);
    }

    // Partial overlap with no clean second full-total subset must be left UNCHANGED — dropping
    // a member here would lose real data. Members: 40 + 35 + 25 (=100, total) PLUS a stray 10
    // that belongs to no second full-total partition, so the set does NOT cleanly partition.
    [Fact]
    public void BuildAxisSeries_NoCleanSecondFullTotalSubset_LeftUnchanged()
    {
        var rows = new List<RevenueBreakdownTools.DimensionalRevenueRow>
        {
            new(Geo, "country:US", Fy2025, 40m, "USD", Filed),
            new(Geo, "country:CN", Fy2025, 35m, "USD", Filed),
            new(Geo, "country:SG", Fy2025, 25m, "USD", Filed),
            new(Geo, "country:TW", Fy2025, 10m, "USD", Filed),
        };

        var (_, _, members) = RevenueBreakdownTools.BuildAxisSeries(
            rows,
            [Geo],
            maxYears: 8,
            Totals(100m)
        );

        members
            .Should()
            .HaveCount(4, "no clean disjoint multi-partition is detectable, so nothing is dropped");
    }
}
