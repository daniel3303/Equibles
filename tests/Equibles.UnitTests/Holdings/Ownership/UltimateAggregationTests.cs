using Equibles.Holdings.BusinessLogic.Ownership;
using Equibles.Holdings.BusinessLogic.Ownership.Models;
using Equibles.Holdings.Data.Models;
using static Equibles.UnitTests.Holdings.Ownership.OwnershipRows;

namespace Equibles.UnitTests.Holdings.Ownership;

public class UltimateAggregationTests
{
    [Fact]
    public void UltimateAggregation_SumsDisjoint13FAndUncontained()
    {
        // FMR files a 13F and so does its subsidiary 35368: the 13F rules keep the two reports
        // disjoint, so both count. The subsidiary 35369's 13G sits inside FMR's report and never
        // counts again; 35370's 13G (owned by FMR, contained in no report) adds to the group.
        var fmr = Form13F("315066", 5_000);
        var sub13F = Form13F("35368", 1_000);
        var contained = Schedule13G("35369", 700, new DateOnly(2026, 8, 1));
        var owned = Schedule13G("35370", 300, new DateOnly(2026, 8, 2));
        var insider = Insider("500", 200);
        var fund = Fund("S000006037", 900, adviserCik: "315066");
        var rows = new List<StockHolderRow> { fmr, sub13F, contained, owned, insider, fund };
        var rollups = new[]
        {
            ReportsThrough("35368", "315066"),
            ReportsThrough("35369", "315066"),
            OwnedBy("35370", "315066"),
        }.ToDictionary(rollup => rollup.Cik);
        StockOwnershipPrecedence.Arrange(
            rows,
            new StockOwnershipPrecedence.Context { Rollups = rollups, Today = Today }
        );
        var summary = OwnershipSummaryCalculator.Build(
            rows,
            new OwnershipSummaryCalculator.Context { SharesOutstanding = 20_000 }
        );

        var groups = UltimateAggregation.Build(rows, rollups, _ => "FMR LLC", 20_000);

        var fmrGroup = groups.Single(group => group.UltimateCik == "315066");
        fmrGroup.Shares.Should().Be(6_300);
        fmrGroup.Form13FShares.Should().Be(6_000);
        fmrGroup.Members.Should().BeEquivalentTo([fmr, sub13F, owned]);
        fmrGroup.PercentOutstanding.Should().Be(31.5m);
        groups.Single(group => group.UltimateCik == "500").Shares.Should().Be(200);
        groups
            .Sum(group => group.Shares)
            .Should()
            .Be(
                summary.Institutional.Shares
                    + summary.Insiders.Shares
                    + summary.Schedule13DGOnly.Shares
            );
    }

    [Fact]
    public void UltimateAggregation_NeverAddsABlockOrInsiderNamingA13FFiler()
    {
        // 777 owns no rollup to FMR, yet its 13D names FMR as a reporting person, and the insider
        // 900 files a 13D naming 777: both report shares FMR's 13F already counts.
        var fmr = Form13F("315066", 5_000);
        var group13D = Schedule13D("777", 5_100, new DateOnly(2026, 8, 1), "777", "315066");
        var insider = Insider("900", 400);
        var insider13D = Schedule13D("900", 450, new DateOnly(2026, 8, 2), "900", "777");
        var rows = new List<StockHolderRow> { fmr, group13D, insider, insider13D };
        var rollups = new Dictionary<string, HolderRollup>();
        StockOwnershipPrecedence.Arrange(
            rows,
            new StockOwnershipPrecedence.Context { Rollups = rollups, Today = Today }
        );
        OwnershipSummaryCalculator.Build(
            rows,
            new OwnershipSummaryCalculator.Context { SharesOutstanding = 20_000 }
        );

        var groups = UltimateAggregation.Build(rows, rollups, cik => cik, 20_000);

        groups.Sum(group => group.Shares).Should().Be(5_000);
        groups.Should().ContainSingle(group => group.UltimateCik == "315066");
    }
}
