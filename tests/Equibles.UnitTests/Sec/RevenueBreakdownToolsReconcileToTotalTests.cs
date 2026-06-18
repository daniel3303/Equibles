using Equibles.Sec.FinancialFacts.Mcp.Tools;
using FluentAssertions;

namespace Equibles.UnitTests.Sec;

public class RevenueBreakdownToolsReconcileToTotalTests
{
    private const string Geo = "srt:StatementGeographicalAxis";
    private static readonly DateOnly Fy2024 = new(2024, 12, 31);
    private static readonly DateOnly OldFiling = new(2025, 2, 1);
    private static readonly DateOnly NewFiling = new(2026, 2, 1);

    private static Dictionary<(DateOnly, string), IReadOnlyList<decimal>> Totals(
        params decimal[] usdTotals
    ) => new() { [(Fy2024, "USD")] = usdTotals };

    // The NVDA/AMD failure mode: a later filing re-disaggregates a period and DROPS a
    // member (Singapore) that an older filing reported. The latest filing's members sum to
    // consolidated total revenue, so it is a complete re-disaggregation — the dropped
    // member must NOT linger from the older filing, or the axis double-counts it.
    [Fact]
    public void BuildAxisSeries_LaterFilingDropsMemberReconcilingToTotal_DropsStaleMember()
    {
        var rows = new List<RevenueBreakdownTools.DimensionalRevenueRow>
        {
            // Older filing: A, B, C, Singapore reconcile to 100 in total.
            new(Geo, "country:US", Fy2024, 30m, "USD", OldFiling),
            new(Geo, "country:TW", Fy2024, 20m, "USD", OldFiling),
            new(Geo, "country:CN", Fy2024, 25m, "USD", OldFiling),
            new(Geo, "country:SG", Fy2024, 25m, "USD", OldFiling),
            // Newer filing: re-disaggregates A, B, C (no Singapore) summing to the SAME 100.
            new(Geo, "country:US", Fy2024, 45m, "USD", NewFiling),
            new(Geo, "country:TW", Fy2024, 25m, "USD", NewFiling),
            new(Geo, "country:CN", Fy2024, 30m, "USD", NewFiling),
        };

        var (unit, periodEnds, members) = RevenueBreakdownTools.BuildAxisSeries(
            rows,
            [Geo],
            maxYears: 8,
            Totals(100m)
        );

        unit.Should().Be("USD");
        periodEnds.Should().Equal(Fy2024);
        members.Should().HaveCount(3, "the dropped member must not linger from the older filing");
        members
            .Select(m => m.Label)
            .Should()
            .NotContain("Singapore", "the latest filing re-disaggregates the period without it");
        members
            .Sum(m => m.Values[0] ?? 0m)
            .Should()
            .Be(100m, "the surviving members reconcile exactly to consolidated total revenue");
    }

    // A genuine partial amendment: the latest filing re-states only ONE member and does not
    // re-report the rest, so its members alone do NOT reconcile to total. The un-amended
    // members must still carry forward from the prior filing (the existing contract).
    [Fact]
    public void BuildAxisSeries_PartialAmendmentNotReconcilingToTotal_KeepsCarriedForwardMembers()
    {
        var rows = new List<RevenueBreakdownTools.DimensionalRevenueRow>
        {
            // Older filing: two members reconcile to 100.
            new(Geo, "country:US", Fy2024, 60m, "USD", OldFiling),
            new(Geo, "country:CN", Fy2024, 40m, "USD", OldFiling),
            // Newer filing: restates only US — does not re-report China, so 70 != 100.
            new(Geo, "country:US", Fy2024, 70m, "USD", NewFiling),
        };

        var (_, _, members) = RevenueBreakdownTools.BuildAxisSeries(
            rows,
            [Geo],
            maxYears: 8,
            Totals(100m)
        );

        members.Should().HaveCount(2, "a partial amendment carries the un-amended member forward");
        // Identify members by the same label the code derives, so the assertion does not depend
        // on the runtime's ICU/CLDR English name for a country (e.g. "China mainland" vs "China").
        members
            .Single(m => m.Label == RevenueBreakdownTools.Humanize("country:US"))
            .Values[0]
            .Should()
            .Be(70m, "the restated value wins for the amended member");
        members
            .Single(m => m.Label == RevenueBreakdownTools.Humanize("country:CN"))
            .Values[0]
            .Should()
            .Be(40m, "the un-amended member carries forward from the prior filing");
    }

    // A filer can tag several revenue concepts — the dimensional axis may disaggregate ASC 606
    // revenue (a subset) while the consolidated total Revenues figure is larger. The members
    // complete the period if they reconcile to ANY candidate total, so the dropped member is
    // still removed even when a bigger unrelated total is also present.
    [Fact]
    public void BuildAxisSeries_ReconcilesToOneOfSeveralConceptTotals_DropsStaleMember()
    {
        var rows = new List<RevenueBreakdownTools.DimensionalRevenueRow>
        {
            // Older filing: members sum to the ASC 606 subtotal of 80, including a stale member.
            new(Geo, "country:US", Fy2024, 30m, "USD", OldFiling),
            new(Geo, "country:CN", Fy2024, 25m, "USD", OldFiling),
            new(Geo, "country:SG", Fy2024, 25m, "USD", OldFiling),
            // Newer filing: re-disaggregates to the SAME 80 without Singapore.
            new(Geo, "country:US", Fy2024, 50m, "USD", NewFiling),
            new(Geo, "country:CN", Fy2024, 30m, "USD", NewFiling),
        };

        // Two candidate totals for the period: total Revenues 100 and the ASC 606 subtotal 80.
        var (_, _, members) = RevenueBreakdownTools.BuildAxisSeries(
            rows,
            [Geo],
            maxYears: 8,
            Totals(100m, 80m)
        );

        members.Should().HaveCount(2, "the members reconcile to the ASC 606 candidate total");
        members
            .Select(m => m.Label)
            .Should()
            .NotContain("Singapore", "the latest filing completes the period without it");
    }
}
