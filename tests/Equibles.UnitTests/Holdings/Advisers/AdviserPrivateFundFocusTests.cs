using Equibles.Holdings.BusinessLogic.Advisers;
using Equibles.Holdings.Data.Models;
using Equibles.Sec.Data.Models;

namespace Equibles.UnitTests.Holdings.Advisers;

public class AdviserPrivateFundFocusTests
{
    private static readonly DateOnly Roster = new(2026, 10, 1);

    [Theory]
    [InlineData(3, 2, 0, PrivateFundFocus.Hedge)]
    [InlineData(2, 2, 0, PrivateFundFocus.Mixed)]
    [InlineData(0, 0, 1, PrivateFundFocus.Other)]
    [InlineData(0, 0, 0, PrivateFundFocus.None)]
    public void ByCount_IsTheTypeHoldingMoreThanHalfTheFunds(
        int hedge,
        int privateEquity,
        int other,
        PrivateFundFocus expected
    )
    {
        AdviserPrivateFundFocus
            .ByCount(new RosterFundCounts(hedge, privateEquity, null, null, null, null, other))
            .Should()
            .Be(expected);
    }

    [Fact]
    public void RecentScheduleD_DecidesByGrossAssetsOverTheRosterCount()
    {
        // Eight small venture funds outnumber two hedge funds, which hold most of the assets.
        var roster = new RosterFundCounts(2, null, 8, null, null, null, null);
        var filing = Filing(new DateOnly(2024, 11, 20), hedge: 900, ventureCapital: 100);

        var (focus, basis, asOf) = AdviserPrivateFundFocus.Decide(
            roster,
            Roster,
            filing,
            new DateOnly(2026, 10, 20)
        );

        focus.Should().Be(PrivateFundFocus.Hedge);
        basis.Should().Be(PrivateFundFocusBasis.GrossAssets);
        asOf.Should().Be(new DateOnly(2024, 11, 20));
    }

    [Fact]
    public void ScheduleDOlderThanTheLookback_FallsBackToTheRosterCount()
    {
        var roster = new RosterFundCounts(2, null, 8, null, null, null, null);
        var filing = Filing(new DateOnly(2024, 3, 28), hedge: 900, ventureCapital: 100);

        var (focus, basis, asOf) = AdviserPrivateFundFocus.Decide(
            roster,
            Roster,
            filing,
            new DateOnly(2026, 10, 20)
        );

        focus.Should().Be(PrivateFundFocus.VentureCapital);
        basis.Should().Be(PrivateFundFocusBasis.FundCount);
        asOf.Should().Be(Roster);
    }

    [Fact]
    public void ScheduleDFiledAfterTheDate_IsNotUsed()
    {
        var filing = Filing(new DateOnly(2022, 9, 1), hedge: 900, ventureCapital: 100);

        var (focus, basis, _) = AdviserPrivateFundFocus.Decide(
            new RosterFundCounts(null, null, null, null, null, null, null),
            new DateOnly(2022, 6, 1),
            filing,
            new DateOnly(2022, 6, 30)
        );

        focus.Should().Be(PrivateFundFocus.None);
        basis.Should().BeNull();
    }

    [Fact]
    public void ByAssets_WithoutAMajorityType_IsMixed()
    {
        AdviserPrivateFundFocus
            .ByAssets(Filing(Roster, hedge: 50, ventureCapital: 50))
            .Should()
            .Be(PrivateFundFocus.Mixed);
    }

    private static FormAdvPrivateFundFiling Filing(
        DateOnly filedOn,
        long hedge,
        long ventureCapital
    ) =>
        new()
        {
            FiledOn = filedOn,
            FundCount = 2,
            GrossAssets = hedge + ventureCapital,
            HedgeFundCount = 1,
            HedgeFundGrossAssets = hedge,
            VentureCapitalFundCount = 1,
            VentureCapitalFundGrossAssets = ventureCapital,
        };
}
