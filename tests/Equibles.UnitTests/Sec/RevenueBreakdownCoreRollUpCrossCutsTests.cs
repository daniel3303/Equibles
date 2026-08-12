using Equibles.Sec.FinancialFacts.BusinessLogic.RevenueBreakdown;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins the cross-cut roll-up's ONE-partner-family rule (EquiblesCommercial#7166). XOM
/// tags geography only in cross-cuts (product × geography AND geography × segment); a
/// roll-up that sums a country's legs from BOTH partner families counts the same revenue
/// once per family — prod published ~2.34× consolidated revenue for XOM's geography axis
/// that way, with Non-US alone at 145% of consolidated. Exactly one partner family may
/// contribute per period, chosen by reconciliation against the consolidated totals.
/// </summary>
public class RevenueBreakdownCoreRollUpCrossCutsTests
{
    private static readonly DateOnly Fy2025End = new(2025, 12, 31);
    private static readonly DateOnly Fy2025Start = new(2025, 1, 1);
    private static readonly DateOnly Filed = new(2026, 2, 18);

    private static CrossCutRevenueRow GeoCross(
        string otherAxis,
        string otherMember,
        string geoMember,
        decimal value,
        DateOnly? filed = null
    ) =>
        new(
            "srt:StatementGeographicalAxis",
            geoMember,
            otherAxis,
            otherMember,
            Fy2025End,
            value,
            "USD",
            filed ?? Filed,
            Fy2025Start,
            2025
        );

    // The XOM FY2025 shape, condensed: a product × geography partition that lands near
    // consolidated total revenue, and a geography × segment partition that overshoots it
    // (segment revenue includes intersegment sales).
    private static List<CrossCutRevenueRow> XomShape() =>
        [
            GeoCross("srt:ProductOrServiceAxis", "xom:SalesMember", "country:US", 137m),
            GeoCross("srt:ProductOrServiceAxis", "xom:SalesMember", "us-gaap:NonUsMember", 186m),
            GeoCross(
                "us-gaap:StatementBusinessSegmentsAxis",
                "xom:EnergyMember",
                "country:US",
                118m
            ),
            GeoCross(
                "us-gaap:StatementBusinessSegmentsAxis",
                "xom:EnergyMember",
                "us-gaap:NonUsMember",
                173m
            ),
            GeoCross(
                "us-gaap:StatementBusinessSegmentsAxis",
                "xom:UpstreamMember",
                "country:US",
                74m
            ),
            GeoCross(
                "us-gaap:StatementBusinessSegmentsAxis",
                "xom:UpstreamMember",
                "us-gaap:NonUsMember",
                87m
            ),
        ];

    private static Dictionary<(DateOnly, string), IReadOnlyList<decimal>> Totals(
        params decimal[] totals
    ) => new() { [(Fy2025End, "USD")] = totals };

    [Fact]
    public void RollUpCrossCuts_TwoPartnerFamilies_NeverMixesLegsAcrossFamilies()
    {
        var rolled = RevenueBreakdownCore.RollUpCrossCuts(
            XomShape(),
            RevenueBreakdownCore.GeographyAxes,
            [],
            Totals(323m)
        );

        // The product partner reconciles (137 + 186 = 323); the segment partner
        // overshoots (192 + 260 = 452). A mixed sum would publish US = 329 / Non-US =
        // 446 — the 2.34× corruption.
        rolled.Should().HaveCount(2);
        rolled.Single(r => r.Member == "country:US").Value.Should().Be(137m);
        rolled.Single(r => r.Member == "us-gaap:NonUsMember").Value.Should().Be(186m);
    }

    [Fact]
    public void RollUpCrossCuts_NoPartnerReconciles_TheClosestPartnerWinsAlone()
    {
        // Neither partner reconciles to 400, but the segment partner (sum 452) deviates
        // less than the product partner (sum 323) — it must win ALONE, never merged.
        var rolled = RevenueBreakdownCore.RollUpCrossCuts(
            XomShape(),
            RevenueBreakdownCore.GeographyAxes,
            [],
            Totals(400m)
        );

        rolled.Should().HaveCount(2);
        rolled.Single(r => r.Member == "country:US").Value.Should().Be(118m + 74m);
        rolled.Single(r => r.Member == "us-gaap:NonUsMember").Value.Should().Be(173m + 87m);
    }

    [Fact]
    public void RollUpCrossCuts_PeriodWithSingleAxisCut_IsNeverRolledUp()
    {
        var singleAxis = new List<DimensionalRevenueRow>
        {
            new(
                "srt:StatementGeographicalAxis",
                "country:US",
                Fy2025End,
                999m,
                "USD",
                Filed,
                Fy2025Start,
                2025
            ),
        };

        var rolled = RevenueBreakdownCore.RollUpCrossCuts(
            XomShape(),
            RevenueBreakdownCore.GeographyAxes,
            singleAxis,
            Totals(323m)
        );

        // A reported single-axis disaggregation always beats a derived roll-up.
        rolled.Should().BeEmpty();
    }

    [Fact]
    public void RollUpCrossCuts_RestatedLeg_LatestFiledLegWinsBeforeTheSum()
    {
        var crossCuts = new List<CrossCutRevenueRow>
        {
            GeoCross(
                "srt:ProductOrServiceAxis",
                "xom:SalesMember",
                "country:US",
                100m,
                new DateOnly(2026, 2, 1)
            ),
            // The same (member, partner-member) leg restated by a later filing.
            GeoCross(
                "srt:ProductOrServiceAxis",
                "xom:SalesMember",
                "country:US",
                120m,
                new DateOnly(2026, 3, 1)
            ),
            GeoCross(
                "srt:ProductOrServiceAxis",
                "xom:ServicesMember",
                "country:US",
                30m,
                new DateOnly(2026, 2, 1)
            ),
        };

        var rolled = RevenueBreakdownCore.RollUpCrossCuts(
            crossCuts,
            RevenueBreakdownCore.GeographyAxes,
            [],
            Totals(150m)
        );

        rolled.Should().ContainSingle().Which.Value.Should().Be(120m + 30m);
    }

    [Fact]
    public void RollUpCrossCuts_NoConsolidatedTotals_PicksDeterministically()
    {
        // Without a total to validate against, the tie breaks on member count then the
        // canonical family order — the pick must be stable, and still one family only.
        var rolled = RevenueBreakdownCore.RollUpCrossCuts(
            XomShape(),
            RevenueBreakdownCore.GeographyAxes,
            [],
            new Dictionary<(DateOnly, string), IReadOnlyList<decimal>>()
        );

        rolled.Should().HaveCount(2);
        // Both families produce 2 members; the segment family precedes the product
        // family in the canonical order.
        rolled.Single(r => r.Member == "country:US").Value.Should().Be(118m + 74m);
        rolled.Single(r => r.Member == "us-gaap:NonUsMember").Value.Should().Be(173m + 87m);
    }
}
