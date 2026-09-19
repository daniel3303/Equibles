using Equibles.Sec.FinancialFacts.BusinessLogic.RevenueBreakdown;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins the arithmetic rename proof inside BuildAxisSeries (XbrlMemberRenames): Palantir
/// tagged pltr:CommercialMember in every 10-K but pltr:CommercialSegmentMember in the
/// FY2024 one, and because ReconcileToTotal keeps each period's latest filing, FY2022
/// survived only under the FY2024 spelling — the pivot showed a "Commercial" row with a
/// hole and a one-cell "Commercial Segment" row. Two spellings are one line only by
/// arithmetic: never tagged side by side in one filing, two or more exactly-equal positive
/// overlapping periods, and no contradicting overlap. Names are never compared.
/// </summary>
public class RevenueBreakdownCoreMemberRenameTests
{
    private const string Axis = "us-gaap:StatementBusinessSegmentsAxis";

    private static readonly DateOnly Fy2021 = new(2021, 12, 31);
    private static readonly DateOnly Fy2022 = new(2022, 12, 31);
    private static readonly DateOnly Fy2023 = new(2023, 12, 31);
    private static readonly DateOnly Fy2024 = new(2024, 12, 31);
    private static readonly DateOnly Fy2025 = new(2025, 12, 31);

    private static DimensionalRevenueRow Row(
        string member,
        DateOnly filed,
        DateOnly periodEnd,
        decimal value,
        string axis = Axis
    ) =>
        new(
            axis,
            member,
            periodEnd,
            value,
            "USD",
            filed,
            new DateOnly(periodEnd.Year, 1, 1),
            periodEnd.Year
        );

    private static Dictionary<(DateOnly, string), IReadOnlyList<decimal>> Totals(
        params (DateOnly PeriodEnd, decimal Total)[] totals
    ) => totals.ToDictionary(t => (t.PeriodEnd, "USD"), t => (IReadOnlyList<decimal>)[t.Total]);

    // Palantir's own filing history: the FY2024 10-K (filed 2025-02-18) respelled the
    // commercial member, and the FY2025 10-K went back to the original spelling.
    private static List<DimensionalRevenueRow> PalantirRows()
    {
        var tenK2023 = new DateOnly(2023, 2, 21);
        var tenK2024 = new DateOnly(2024, 2, 20);
        var tenK2025 = new DateOnly(2025, 2, 18);
        var tenK2026 = new DateOnly(2026, 2, 17);
        return
        [
            Row("pltr:GovernmentOperatingSegmentMember", tenK2023, Fy2021, 897_356_000m),
            Row("pltr:GovernmentOperatingSegmentMember", tenK2023, Fy2022, 1_071_776_000m),
            Row("pltr:GovernmentOperatingSegmentMember", tenK2024, Fy2021, 897_356_000m),
            Row("pltr:GovernmentOperatingSegmentMember", tenK2024, Fy2022, 1_071_776_000m),
            Row("pltr:GovernmentOperatingSegmentMember", tenK2024, Fy2023, 1_222_215_000m),
            Row("pltr:GovernmentOperatingSegmentMember", tenK2025, Fy2022, 1_071_776_000m),
            Row("pltr:GovernmentOperatingSegmentMember", tenK2025, Fy2023, 1_222_215_000m),
            Row("pltr:GovernmentOperatingSegmentMember", tenK2025, Fy2024, 1_569_605_000m),
            Row("pltr:GovernmentOperatingSegmentMember", tenK2026, Fy2023, 1_222_215_000m),
            Row("pltr:GovernmentOperatingSegmentMember", tenK2026, Fy2024, 1_569_605_000m),
            Row("pltr:GovernmentOperatingSegmentMember", tenK2026, Fy2025, 2_402_287_000m),
            Row("pltr:CommercialMember", tenK2023, Fy2021, 644_533_000m),
            Row("pltr:CommercialMember", tenK2023, Fy2022, 834_095_000m),
            Row("pltr:CommercialMember", tenK2024, Fy2021, 644_533_000m),
            Row("pltr:CommercialMember", tenK2024, Fy2022, 834_095_000m),
            Row("pltr:CommercialMember", tenK2024, Fy2023, 1_002_797_000m),
            Row("pltr:CommercialSegmentMember", tenK2025, Fy2022, 834_095_000m),
            Row("pltr:CommercialSegmentMember", tenK2025, Fy2023, 1_002_797_000m),
            Row("pltr:CommercialSegmentMember", tenK2025, Fy2024, 1_295_902_000m),
            Row("pltr:CommercialMember", tenK2026, Fy2023, 1_002_797_000m),
            Row("pltr:CommercialMember", tenK2026, Fy2024, 1_295_902_000m),
            Row("pltr:CommercialMember", tenK2026, Fy2025, 2_073_159_000m),
        ];
    }

    private static Dictionary<(DateOnly, string), IReadOnlyList<decimal>> PalantirTotals() =>
        Totals(
            (Fy2021, 1_541_889_000m),
            (Fy2022, 1_905_871_000m),
            (Fy2023, 2_225_012_000m),
            (Fy2024, 2_865_507_000m),
            (Fy2025, 4_475_446_000m)
        );

    [Fact]
    public void BuildAxisSeries_ARespelledMemberIsOneLineWithNoHole()
    {
        var series = RevenueBreakdownCore.BuildAxisSeries(
            PalantirRows(),
            RevenueBreakdownCore.SegmentAxes,
            5,
            PalantirTotals()
        );

        series.Members.Should().HaveCount(2);
        var commercial = series.Members.Single(m => m.Member == "pltr:CommercialMember");
        commercial
            .Values.Should()
            .Equal(644_533_000m, 834_095_000m, 1_002_797_000m, 1_295_902_000m, 2_073_159_000m);
        series.Members.Should().NotContain(m => m.Member == "pltr:CommercialSegmentMember");
    }

    [Fact]
    public void Unify_RewritesToTheLatestFiledSpellingOnly()
    {
        var unified = XbrlMemberRenames.Unify(PalantirRows());

        unified.Should().HaveCount(22, "a rename rewrites members, it never drops or adds rows");
        unified.Should().NotContain(r => r.Member == "pltr:CommercialSegmentMember");
        unified
            .Where(r => r.Member == "pltr:GovernmentOperatingSegmentMember")
            .Should()
            .HaveCount(11, "an unrenamed member is untouched");
        // The FY2024 10-K's rows now carry the spelling the issuer filed last.
        unified
            .Where(r =>
                r.FiledDate == new DateOnly(2025, 2, 18) && r.Member == "pltr:CommercialMember"
            )
            .Select(r => r.Value)
            .Should()
            .BeEquivalentTo([834_095_000m, 1_002_797_000m, 1_295_902_000m]);
    }

    [Fact]
    public void Unify_OneEqualOverlapIsNotProof()
    {
        var rows = new List<DimensionalRevenueRow>
        {
            Row("x:OldMember", new DateOnly(2024, 2, 1), Fy2022, 100m),
            Row("x:OldMember", new DateOnly(2024, 2, 1), Fy2023, 120m),
            Row("x:NewMember", new DateOnly(2025, 2, 1), Fy2023, 120m),
            Row("x:NewMember", new DateOnly(2025, 2, 1), Fy2024, 150m),
        };

        XbrlMemberRenames
            .Unify(rows)
            .Select(r => r.Member)
            .Distinct()
            .Should()
            .HaveCount(2, "a single equal year can be a round-value coincidence");
    }

    [Fact]
    public void Unify_AContradictingOverlapDisprovesThePair()
    {
        var rows = new List<DimensionalRevenueRow>
        {
            Row("x:OldMember", new DateOnly(2024, 2, 1), Fy2021, 90m),
            Row("x:OldMember", new DateOnly(2024, 2, 1), Fy2022, 100m),
            Row("x:OldMember", new DateOnly(2024, 2, 1), Fy2023, 120m),
            Row("x:NewMember", new DateOnly(2025, 2, 1), Fy2021, 90m),
            Row("x:NewMember", new DateOnly(2025, 2, 1), Fy2022, 100m),
            // A re-segmentation: the restated FY2023 is a different number.
            Row("x:NewMember", new DateOnly(2025, 2, 1), Fy2023, 130m),
            Row("x:NewMember", new DateOnly(2025, 2, 1), Fy2024, 150m),
        };

        XbrlMemberRenames
            .Unify(rows)
            .Select(r => r.Member)
            .Distinct()
            .Should()
            .HaveCount(
                2,
                "one unequal overlap is two different measures however many others agree"
            );
    }

    [Fact]
    public void Unify_MembersTaggedSideBySideInOneFilingAreTwoMembers()
    {
        var filing = new DateOnly(2025, 2, 1);
        var rows = new List<DimensionalRevenueRow>
        {
            // Both spellings in the SAME filing, with equal values in two years — two
            // real members (or a subtotal), never a rename.
            Row("x:AMember", filing, Fy2023, 100m),
            Row("x:AMember", filing, Fy2024, 110m),
            Row("x:BMember", filing, Fy2023, 100m),
            Row("x:BMember", filing, Fy2024, 110m),
        };

        XbrlMemberRenames.Unify(rows).Select(r => r.Member).Distinct().Should().HaveCount(2);
    }

    [Fact]
    public void Unify_ZeroOverlapsProveNothing()
    {
        var rows = new List<DimensionalRevenueRow>
        {
            Row("x:OldMember", new DateOnly(2024, 2, 1), Fy2022, 0m),
            Row("x:OldMember", new DateOnly(2024, 2, 1), Fy2023, 0m),
            Row("x:NewMember", new DateOnly(2025, 2, 1), Fy2022, 0m),
            Row("x:NewMember", new DateOnly(2025, 2, 1), Fy2023, 0m),
            Row("x:NewMember", new DateOnly(2025, 2, 1), Fy2024, 50m),
        };

        XbrlMemberRenames.Unify(rows).Select(r => r.Member).Distinct().Should().HaveCount(2);
    }

    [Fact]
    public void Unify_ProvenPairsChainTransitivelyToTheLatestFiledSpelling()
    {
        var rows = new List<DimensionalRevenueRow>
        {
            Row("x:FirstMember", new DateOnly(2023, 2, 1), Fy2021, 10m),
            Row("x:FirstMember", new DateOnly(2023, 2, 1), Fy2022, 20m),
            Row("x:SecondMember", new DateOnly(2024, 2, 1), Fy2021, 10m),
            Row("x:SecondMember", new DateOnly(2024, 2, 1), Fy2022, 20m),
            Row("x:SecondMember", new DateOnly(2024, 2, 1), Fy2023, 30m),
            Row("x:ThirdMember", new DateOnly(2025, 2, 1), Fy2022, 20m),
            Row("x:ThirdMember", new DateOnly(2025, 2, 1), Fy2023, 30m),
            Row("x:ThirdMember", new DateOnly(2025, 2, 1), Fy2024, 40m),
        };

        var unified = XbrlMemberRenames.Unify(rows);

        unified
            .Select(r => r.Member)
            .Distinct()
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be("x:ThirdMember");
    }

    [Fact]
    public void Unify_AContradictionInsideAChainDissolvesTheWholeGroup()
    {
        var rows = new List<DimensionalRevenueRow>
        {
            Row("x:FirstMember", new DateOnly(2023, 2, 1), Fy2021, 10m),
            Row("x:FirstMember", new DateOnly(2023, 2, 1), Fy2022, 20m),
            Row("x:FirstMember", new DateOnly(2023, 2, 1), Fy2023, 31m),
            Row("x:SecondMember", new DateOnly(2024, 2, 1), Fy2021, 10m),
            Row("x:SecondMember", new DateOnly(2024, 2, 1), Fy2022, 20m),
            Row("x:ThirdMember", new DateOnly(2025, 2, 1), Fy2021, 10m),
            Row("x:ThirdMember", new DateOnly(2025, 2, 1), Fy2022, 20m),
            // First and Third disagree on FY2023 while each matches Second on two years.
            Row("x:ThirdMember", new DateOnly(2025, 2, 1), Fy2023, 30m),
        };

        XbrlMemberRenames
            .Unify(rows)
            .Select(r => r.Member)
            .Distinct()
            .Should()
            .HaveCount(3, "the arithmetic cannot say which spelling is which");
    }

    [Fact]
    public void Unify_TwoCoFiledMembersNeverMergeThroughABridgingSpelling()
    {
        // A and B are tagged side by side in the FY2023 10-K with equal figures (a
        // one-component subtotal); C alone in the FY2024 10-K equals both. C proves
        // against each, but A and B are two members by construction, so the group
        // dissolves and all three stay as tagged.
        var rows = new List<DimensionalRevenueRow>
        {
            Row("x:AMember", Fy2023, Fy2022, 100m),
            Row("x:AMember", Fy2023, Fy2023, 200m),
            Row("x:BMember", Fy2023, Fy2022, 100m),
            Row("x:BMember", Fy2023, Fy2023, 200m),
            Row("x:CMember", Fy2024, Fy2022, 100m),
            Row("x:CMember", Fy2024, Fy2023, 200m),
            Row("x:CMember", Fy2024, Fy2024, 300m),
        };

        var unified = XbrlMemberRenames.Unify(rows);

        unified.Select(r => r.Member).Distinct().Should().HaveCount(3);
        unified.Should().BeEquivalentTo(rows);
    }

    [Fact]
    public void Unify_AnAxisWiderThanTheMemberCapIsReturnedAsTagged()
    {
        var rows = Enumerable
            .Range(0, XbrlMemberRenames.MaxMembers + 1)
            .SelectMany(i =>
                new[]
                {
                    Row($"x:Member{i}", Fy2023, Fy2022, 100m + i),
                    Row($"x:Member{i}", Fy2023, Fy2023, 200m + i),
                }
            )
            .Concat(
                new[]
                {
                    Row("x:Renamed0", Fy2024, Fy2022, 100m),
                    Row("x:Renamed0", Fy2024, Fy2023, 200m),
                }
            )
            .ToList();

        var unified = XbrlMemberRenames.Unify(rows);

        unified.Should().BeEquivalentTo(rows, "the pairwise proof is not run past the cap");
    }

    // The FY2023 10-K states FY2022 Commercial twice, once with the consolidation
    // qualifier and once without, with different figures. That period proves nothing
    // and contradicts nothing; the two clean overlaps still prove the rename, and the
    // answer is the same whichever row the query happened to hand over first.
    [Fact]
    public void Unify_APeriodOneFilingStatesTwiceWithTwoFiguresIsNoEvidenceEitherWay()
    {
        var rows = new List<DimensionalRevenueRow>
        {
            Row("pltr:CommercialMember", Fy2023.AddDays(50), Fy2022, 834_095_000m),
            Row("pltr:CommercialMember", Fy2023.AddDays(50), Fy2022, 900_000_000m),
            Row("pltr:CommercialMember", Fy2023.AddDays(50), Fy2023, 1_002_797_000m),
            Row("pltr:CommercialMember", Fy2022.AddDays(50), Fy2021, 644_533_000m),
            Row("pltr:CommercialSegmentMember", Fy2024.AddDays(50), Fy2021, 644_533_000m),
            Row("pltr:CommercialSegmentMember", Fy2024.AddDays(50), Fy2022, 834_095_000m),
            Row("pltr:CommercialSegmentMember", Fy2024.AddDays(50), Fy2023, 1_002_797_000m),
            Row("pltr:CommercialSegmentMember", Fy2024.AddDays(50), Fy2024, 1_295_902_000m),
        };
        var reversed = Enumerable.Reverse(rows).ToList();

        var forward = XbrlMemberRenames.Unify(rows);
        var backward = XbrlMemberRenames.Unify(reversed);

        forward.Select(r => r.Member).Distinct().Should().Equal("pltr:CommercialSegmentMember");
        backward.Select(r => r.Member).Distinct().Should().Equal("pltr:CommercialSegmentMember");
        forward.Should().HaveCount(rows.Count, "every row is returned, in its input order");
        forward.Select(r => r.Value).Should().Equal(rows.Select(r => r.Value));
    }

    [Fact]
    public void Unify_AnAmbiguousPeriodAloneProvesNothing()
    {
        var rows = new List<DimensionalRevenueRow>
        {
            Row("x:AMember", Fy2023, Fy2022, 100m),
            Row("x:AMember", Fy2023, Fy2022, 120m),
            Row("x:AMember", Fy2023, Fy2023, 200m),
            Row("x:BMember", Fy2024, Fy2022, 100m),
            Row("x:BMember", Fy2024, Fy2023, 200m),
            Row("x:BMember", Fy2024, Fy2024, 300m),
        };

        var unified = XbrlMemberRenames.Unify(rows);

        unified
            .Select(r => r.Member)
            .Distinct()
            .Should()
            .HaveCount(2, "FY2023 is the only clean overlap and one is not proof");
    }

    [Fact]
    public void BuildSegmentMarginSeries_ARenameProvenOnRevenueAloneYieldsNoCell()
    {
        // Revenue proves the rename (two equal overlaps); operating income carries the
        // old spelling only, so its representative differs and no margin cell can join.
        // Absence, never a cell from a name match.
        var revenueRows = new List<DimensionalRevenueRow>
        {
            Row("x:OldMember", new DateOnly(2024, 2, 1), Fy2022, 100m),
            Row("x:OldMember", new DateOnly(2024, 2, 1), Fy2023, 120m),
            Row("x:NewMember", new DateOnly(2025, 2, 1), Fy2022, 100m),
            Row("x:NewMember", new DateOnly(2025, 2, 1), Fy2023, 120m),
            Row("x:NewMember", new DateOnly(2025, 2, 1), Fy2024, 150m),
        };
        var incomeRows = new List<DimensionalRevenueRow>
        {
            Row("x:OldMember", new DateOnly(2024, 2, 1), Fy2022, 10m),
            Row("x:OldMember", new DateOnly(2024, 2, 1), Fy2023, 12m),
        };
        var revenue = RevenueBreakdownCore.BuildAxisSeries(
            revenueRows,
            RevenueBreakdownCore.SegmentAxes,
            5,
            Totals((Fy2022, 100m), (Fy2023, 120m), (Fy2024, 150m))
        );
        var income = RevenueBreakdownCore.BuildAxisSeries(
            incomeRows,
            RevenueBreakdownCore.SegmentAxes,
            5,
            new Dictionary<(DateOnly, string), IReadOnlyList<decimal>>()
        );

        revenue.Members.Should().ContainSingle().Which.Member.Should().Be("x:NewMember");
        var margin = RevenueBreakdownCore.BuildSegmentMarginSeries(revenue, income);
        margin.Members.Should().BeEmpty();
    }
}
