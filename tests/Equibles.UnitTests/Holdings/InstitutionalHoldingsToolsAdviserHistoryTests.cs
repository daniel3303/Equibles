using Equibles.Holdings.BusinessLogic.Advisers;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Sec.Data.Models;

namespace Equibles.UnitTests.Holdings;

public class InstitutionalHoldingsToolsAdviserHistoryTests
{
    [Fact]
    public void Changes_MergeRunsWithTheSameVisibleFiguresNewestFirst()
    {
        // Only the filing date moved in February, so January and February read as one run.
        var timeline = new List<AdviserFacts>
        {
            Facts(new(2020, 1, 1), new(2020, 1, 1), 1_000),
            Facts(new(2020, 2, 1), new(2020, 2, 1), 1_000),
            Facts(new(2020, 3, 1), new(2020, 5, 1), 2_000),
        };

        var changes = AdviserHistory.Changes(timeline);

        changes
            .Select(facts => (facts.Since, facts.Until, facts.RegulatoryAum))
            .Should()
            .Equal(
                (new DateOnly(2020, 3, 1), new DateOnly(2020, 5, 1), 2_000L),
                (new DateOnly(2020, 1, 1), new DateOnly(2020, 2, 1), 1_000L)
            );
    }

    [Fact]
    public void RenderAdviserHistory_ShowsEachRunAndTheDatedScheduleD()
    {
        var changes = AdviserHistory.Changes([
            Facts(new(2020, 1, 1), new(2020, 2, 1), 1_000),
            Facts(new(2024, 3, 1), new(2026, 10, 1), 2_500_000),
        ]);
        var scheduleD = new FormAdvPrivateFundFiling
        {
            Crd = 111,
            FiledOn = new DateOnly(2024, 3, 27),
            FundCount = 3,
            GrossAssets = 1_150,
            HedgeFundCount = 2,
            HedgeFundGrossAssets = 1_000,
            PrivateEquityFundCount = 1,
            PrivateEquityFundGrossAssets = 150,
        };

        var text = InstitutionalHoldingsTools.RenderAdviserHistory(111, changes, scheduleD);

        text.Should().Contain("**Form ADV history** (CRD 111");
        text.IndexOf("2024-03-01 to 2026-10-01", StringComparison.Ordinal)
            .Should()
            .BeLessThan(text.IndexOf("2020-01-01 to 2020-02-01", StringComparison.Ordinal));
        text.Should().Contain("$2,500,000");
        text.Should().Contain("Pooled investment vehicles 70.0%");
        text.Should().Contain("as filed 2024-03-27");
        text.Should()
            .Contain(
                "3 funds, $1,150 gross assets; Hedge funds $1,000 (2), Private equity funds $150 (1)."
            );
    }

    [Fact]
    public void RenderAdviserHistory_NothingRecorded_IsEmpty()
    {
        InstitutionalHoldingsTools.RenderAdviserHistory(111, [], null).Should().BeEmpty();
    }

    [Fact]
    public void RenderAdviserHistory_LongHistory_ShowsTheNewestRowsAndCountsTheRest()
    {
        var changes = AdviserHistory.Changes(
            Enumerable
                .Range(0, InstitutionalHoldingsTools.AdviserHistoryRows + 3)
                .Select(month =>
                {
                    var date = new DateOnly(2020, 1, 1).AddMonths(month);
                    return Facts(date, date, 1_000 + month);
                })
                .ToList()
        );

        var text = InstitutionalHoldingsTools.RenderAdviserHistory(111, changes, null);

        text.Should().Contain("_3 earlier changes not shown._");
        text.Should().NotContain("2020-01-01 to");
    }

    private static AdviserFacts Facts(DateOnly since, DateOnly until, long aum) =>
        new()
        {
            Crd = 111,
            Registration = FormAdvRegistration.Registered,
            RosterDate = until,
            Since = since,
            Until = until,
            RegulatoryAum = aum,
            ClientBase = HolderClientBase.PooledInvestmentVehicles,
            ClientBaseAumShare = 0.7m,
            PooledVehicleAumShare = 0.7m,
            AdvisesPrivateFunds = true,
            PrivateFundCount = 3,
        };
}
