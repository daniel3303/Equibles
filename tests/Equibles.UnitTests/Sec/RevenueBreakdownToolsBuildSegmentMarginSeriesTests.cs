using Equibles.Sec.FinancialFacts.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

public class RevenueBreakdownToolsBuildSegmentMarginSeriesTests
{
    [Fact]
    public void BuildSegmentMarginSeries_MatchesFoldedQNameAndPeriod_WithPositiveRevenueOnly()
    {
        var fy2023 = new DateOnly(2023, 12, 31);
        var fy2024 = new DateOnly(2024, 12, 31);
        var fy2025 = new DateOnly(2025, 12, 31);
        var fy2023Start = new DateOnly(2023, 1, 1);
        var fy2024Start = new DateOnly(2024, 1, 1);
        var fy2025Start = new DateOnly(2025, 1, 1);
        var revenue = new RevenueBreakdownTools.AxisSeries(
            "USD",
            [fy2023, fy2024],
            [
                new("acme:Cloud_Member", "Cloud", [100m, 200m], [fy2023Start, fy2024Start]),
                new("sales:SharedMember", "Shared", [50m, 50m], [fy2023Start, fy2024Start]),
                new("acme:ZeroMember", "Zero", [0m, 0m], [fy2023Start, fy2024Start]),
            ]
        );
        var income = new RevenueBreakdownTools.AxisSeries(
            "USD",
            [fy2024, fy2025],
            [
                new("ACME:CloudMember", "Cloud", [50m, 999m], [fy2024Start, fy2025Start]),
                new("profit:SharedMember", "Shared", [20m, 20m], [fy2024Start, fy2025Start]),
                new("acme:ZeroMember", "Zero", [10m, 10m], [fy2024Start, fy2025Start]),
            ]
        );

        var result = RevenueBreakdownTools.BuildSegmentMarginSeries(revenue, income);

        result.Unit.Should().Be("%");
        result
            .PeriodEnds.Should()
            .ContainSingle("only an exact shared period is computable")
            .Which.Should()
            .Be(fy2024);
        result.Members.Should().ContainSingle();
        result.Members[0].Member.Should().Be("acme:Cloud_Member");
        result.Members[0].Values.Should().Equal(25m);
        result
            .Members.Should()
            .NotContain(
                m => m.Label == "Shared",
                "equal display labels from different member QNames are not the same segment"
            );
        result
            .Members.Should()
            .NotContain(m => m.Label == "Zero", "non-positive revenue is never a denominator");
    }

    [Fact]
    public void BuildSegmentMarginSeries_SameEndButDifferentDuration_DoesNotDivide()
    {
        var end = new DateOnly(2024, 12, 31);
        var revenue = new RevenueBreakdownTools.AxisSeries(
            "USD",
            [end],
            [new("acme:CloudMember", "Cloud", [200m], [new DateOnly(2024, 1, 1)])]
        );
        var income = new RevenueBreakdownTools.AxisSeries(
            "USD",
            [end],
            [new("acme:CloudMember", "Cloud", [50m], [new DateOnly(2023, 10, 1)])]
        );

        var result = RevenueBreakdownTools.BuildSegmentMarginSeries(revenue, income);

        result.Members.Should().BeEmpty("an exact period match includes both duration endpoints");
    }

    [Fact]
    public void BuildSegmentMarginSeries_DifferentCurrencies_DoesNotDivide()
    {
        var end = new DateOnly(2024, 12, 31);
        var start = new DateOnly(2024, 1, 1);
        var revenue = new RevenueBreakdownTools.AxisSeries(
            "USD",
            [end],
            [new("acme:CloudMember", "Cloud", [200m], [start])]
        );
        var income = new RevenueBreakdownTools.AxisSeries(
            "EUR",
            [end],
            [new("acme:CloudMember", "Cloud", [50m], [start])]
        );

        var result = RevenueBreakdownTools.BuildSegmentMarginSeries(revenue, income);

        result.Members.Should().BeEmpty("a ratio across different currencies is meaningless");
    }
}
