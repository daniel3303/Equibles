using Equibles.Sec.FinancialFacts.BusinessLogic.RevenueBreakdown;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins the arithmetic rollup-member filter inside BuildAxisSeries
/// (EquiblesCommercial#7166): AAPL tags the parent us-gaap:ProductMember alongside its
/// component product lines — the parent equals the exact sum of its children in every
/// period, so a consumer that sums the axis double-counts. The parent must be dropped by
/// arithmetic proof (exact same-subset match across 2+ periods), never by name matching;
/// a single-period coincidence keeps the member.
/// </summary>
public class RevenueBreakdownCoreArithmeticRollupMemberTests
{
    private static readonly DateOnly Fy2024 = new(2024, 9, 28);
    private static readonly DateOnly Fy2025 = new(2025, 9, 27);

    private static DimensionalRevenueRow Row(string member, DateOnly periodEnd, decimal value) =>
        new(
            "srt:ProductOrServiceAxis",
            member,
            periodEnd,
            value,
            "USD",
            new DateOnly(2025, 11, 1),
            periodEnd.AddDays(-364),
            periodEnd.Year
        );

    [Fact]
    public void BuildAxisSeries_ParentEqualToChildSumInEveryPeriod_IsDroppedAsARollup()
    {
        var rows = new List<DimensionalRevenueRow>
        {
            // The parent line: exactly the sum of the three product children, both years.
            Row("us-gaap:ProductMember", Fy2024, 60m),
            Row("us-gaap:ProductMember", Fy2025, 66m),
            Row("aapl:IPhoneMember", Fy2024, 40m),
            Row("aapl:IPhoneMember", Fy2025, 44m),
            Row("aapl:MacMember", Fy2024, 12m),
            Row("aapl:MacMember", Fy2025, 13m),
            Row("aapl:IPadMember", Fy2024, 8m),
            Row("aapl:IPadMember", Fy2025, 9m),
            Row("us-gaap:ServiceMember", Fy2024, 25m),
            Row("us-gaap:ServiceMember", Fy2025, 30m),
        };

        var series = RevenueBreakdownCore.BuildAxisSeries(
            rows,
            RevenueBreakdownCore.ProductAxes,
            8,
            new Dictionary<(DateOnly, string), IReadOnlyList<decimal>>
            {
                [(Fy2024, "USD")] = [85m],
                [(Fy2025, "USD")] = [96m],
            }
        );

        series.Members.Select(m => m.Member).Should().NotContain("us-gaap:ProductMember");
        series
            .Members.Select(m => m.Member)
            .Should()
            .BeEquivalentTo([
                "aapl:IPhoneMember",
                "aapl:MacMember",
                "aapl:IPadMember",
                "us-gaap:ServiceMember",
            ]);
    }

    [Fact]
    public void BuildAxisSeries_SinglePeriodCoincidence_KeepsTheMember()
    {
        var rows = new List<DimensionalRevenueRow>
        {
            // Matches iPhone + Mac exactly in FY2024 only (40 + 12); its FY2025 value
            // matches no subset — coincidence, not a rollup.
            Row("aapl:AccessoriesMember", Fy2024, 52m),
            Row("aapl:AccessoriesMember", Fy2025, 58m),
            Row("aapl:IPhoneMember", Fy2024, 40m),
            Row("aapl:IPhoneMember", Fy2025, 44m),
            Row("aapl:MacMember", Fy2024, 12m),
            Row("aapl:MacMember", Fy2025, 13m),
        };

        var series = RevenueBreakdownCore.BuildAxisSeries(
            rows,
            RevenueBreakdownCore.ProductAxes,
            8,
            new Dictionary<(DateOnly, string), IReadOnlyList<decimal>>()
        );

        series.Members.Select(m => m.Member).Should().Contain("aapl:AccessoriesMember");
    }
}
