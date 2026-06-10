using Equibles.Sec.FinancialFacts.Mcp.Tools;
using FluentAssertions;

namespace Equibles.UnitTests.Sec;

public class RevenueBreakdownToolsBuildAxisSeriesTests
{
    private static readonly DateOnly Fy2023 = new(2023, 12, 31);
    private static readonly DateOnly Fy2024 = new(2024, 12, 31);

    [Fact]
    public void BuildAxisSeries_RestatedAndForeignAxisRows_PivotsLatestFiledPerMemberOldestFirst()
    {
        // Pins the pivot the GetRevenueBreakdown tables are built from: only the
        // requested axes contribute, the latest-filed fact wins a restated
        // (member, period) cell, columns read oldest-first, and a member missing a
        // year carries null (rendered as a dash) — never a fabricated zero.
        var rows = new List<RevenueBreakdownTools.DimensionalRevenueRow>
        {
            new(
                "us-gaap:StatementBusinessSegmentsAxis",
                "acme:CloudMember",
                Fy2023,
                60m,
                "USD",
                new DateOnly(2024, 2, 1)
            ),
            new(
                "us-gaap:StatementBusinessSegmentsAxis",
                "acme:CloudMember",
                Fy2024,
                80m,
                "USD",
                new DateOnly(2025, 2, 1)
            ),
            // Restatement of FY2024 — latest filed must win.
            new(
                "us-gaap:StatementBusinessSegmentsAxis",
                "acme:CloudMember",
                Fy2024,
                90m,
                "USD",
                new DateOnly(2025, 4, 1)
            ),
            // Second member, FY2024 only — FY2023 cell must be null.
            new(
                "us-gaap:StatementBusinessSegmentsAxis",
                "acme:HardwareMember",
                Fy2024,
                40m,
                "USD",
                new DateOnly(2025, 2, 1)
            ),
            // Geography row — not part of the segment axis bucket.
            new(
                "srt:StatementGeographicalAxis",
                "country:US",
                Fy2024,
                70m,
                "USD",
                new DateOnly(2025, 2, 1)
            ),
        };

        var (unit, periodEnds, members) = RevenueBreakdownTools.BuildAxisSeries(
            rows,
            ["us-gaap:StatementBusinessSegmentsAxis"],
            maxYears: 8
        );

        unit.Should().Be("USD");
        periodEnds.Should().Equal(Fy2023, Fy2024);
        members.Should().HaveCount(2, "the geography row belongs to another axis");
        members[0].Label.Should().Be("Cloud", "members order by latest-period value");
        members[0].Values.Should().Equal(60m, 90m);
        members[1].Values.Should().Equal(null, 40m);
    }
}
