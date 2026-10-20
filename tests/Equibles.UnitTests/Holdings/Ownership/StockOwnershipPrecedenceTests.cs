using Equibles.Holdings.BusinessLogic.Ownership;
using Equibles.Holdings.BusinessLogic.Ownership.Models;
using Equibles.Holdings.Data.Models;
using static Equibles.UnitTests.Holdings.Ownership.OwnershipRows;

namespace Equibles.UnitTests.Holdings.Ownership;

public class StockOwnershipPrecedenceTests
{
    private const string BlackRock = "2012383";
    private const string Russell = "1692234";
    private const string Fmr = "315066";
    private const string SpunOut = "777";

    private static List<StockHolderRow> Arrange(
        IReadOnlyList<StockHolderRow> rows,
        Func<string, DateOnly, bool> filedOwn13F = null,
        params HolderRollup[] rollups
    ) =>
        StockOwnershipPrecedence.Arrange(
            rows,
            new StockOwnershipPrecedence.Context
            {
                Rollups = rollups.ToDictionary(rollup => rollup.Cik),
                FiledOwn13F = filedOwn13F ?? ((_, _) => false),
                Today = Today,
            }
        );

    [Fact]
    public void Newer13DGWins_TheHoldersFresher13GLeadsAndIts13FNests()
    {
        var form13F = Form13F("100", 1_000);
        var schedule13G = Schedule13G("100", 1_400, new DateOnly(2026, 8, 5));

        var tops = Arrange([form13F, schedule13G]);

        tops.Should().ContainSingle().Which.Should().BeSameAs(schedule13G);
        schedule13G.Children.Should().ContainSingle().Which.Should().BeSameAs(form13F);
        form13F.ParentRowKey.Should().Be(schedule13G.RowKey);
        schedule13G.Covering13FCik.Should().Be("100");
    }

    [Fact]
    public void Newer13DGWins_OnATieThe13FLeads()
    {
        // Same as-of date and filing date: the 13F leads the 13D or 13G.
        var form13F = Form13F("100", 1_000);
        var schedule13G = Schedule13G("100", 1_400, Q2);

        Arrange([schedule13G, form13F]).Should().ContainSingle().Which.Should().BeSameAs(form13F);
    }

    [Fact]
    public void SubsidiaryGNests_UnderTheParent13FItsConfirmedRollupNames()
    {
        var parent = Form13F(Fmr, 50_000);
        var subsidiary = Schedule13G("35368", 9_000, new DateOnly(2026, 8, 1));

        var tops = Arrange([parent, subsidiary], null, ReportsThrough("35368", Fmr));

        tops.Should().ContainSingle().Which.Should().BeSameAs(parent);
        subsidiary.ParentRowKey.Should().Be(parent.RowKey);
        subsidiary.Covering13FCik.Should().Be(Fmr);
    }

    [Fact]
    public void SubsidiaryGNests_A13GOlderThanTheParentsQuarterIsStale()
    {
        var parent = Form13F(Fmr, 50_000);
        var subsidiary = Schedule13G("35368", 9_000, new DateOnly(2026, 2, 1));

        var visible = StockOwnershipPrecedence.Visible(
            Arrange([parent, subsidiary], null, ReportsThrough("35368", Fmr))
        );

        subsidiary.Has(StockHolderRowFlags.Stale).Should().BeTrue();
        visible.Should().ContainSingle().Which.Children.Should().BeEmpty();
    }

    [Fact]
    public void ThirdPartyListingBlackRocks028_NeverNestsBlackRocks13G()
    {
        // Russell's 13F lists BlackRock, Inc.'s file number, but BlackRock never confirms it, so
        // its rollup names no reporting parent and its 13G stays its own holder.
        var russell = Form13F(Russell, 80_000);
        var blackRock = Schedule13G(BlackRock, 600_000, new DateOnly(2026, 8, 1));
        var blackRockRollup = new HolderRollup
        {
            Cik = BlackRock,
            UltimateCik = BlackRock,
            Basis = HolderRollupBasis.Self,
        };

        var tops = Arrange([russell, blackRock], null, blackRockRollup);

        tops.Should().HaveCount(2);
        blackRock.ParentRowKey.Should().BeNull();
        blackRock.Covering13FCik.Should().BeNull();
        russell.Children.Should().BeEmpty();
    }

    [Fact]
    public void AChildThatFiledItsOwn13FForTheQuarter_IsItsOwnHolder()
    {
        // 777's notice the quarter before still counts it inside FMR's report for Q2, but 777
        // filed a full 13F of its own for Q2, so its 13G is neither nested nor hidden.
        var fmr = Form13F(Fmr, 50_000);
        var spunOut = Schedule13G(SpunOut, 7_000, new DateOnly(2026, 8, 1));

        var tops = Arrange(
            [fmr, spunOut],
            (cik, quarter) => cik == SpunOut && quarter == Q2,
            ReportsThrough(SpunOut, Fmr)
        );

        tops.Should().HaveCount(2).And.Contain(spunOut);
        spunOut.ParentRowKey.Should().BeNull();
        spunOut.Covering13FCik.Should().BeNull();
        spunOut.Has(StockHolderRowFlags.Stale).Should().BeFalse();
    }

    [Fact]
    public void AChildWithoutItsOwn13FForTheQuarter_NestsUnderTheParent()
    {
        var fmr = Form13F(Fmr, 50_000);
        var spunOut = Schedule13G(SpunOut, 7_000, new DateOnly(2026, 8, 1));

        var tops = Arrange([fmr, spunOut], (cik, quarter) => false, ReportsThrough(SpunOut, Fmr));

        tops.Should().ContainSingle().Which.Should().BeSameAs(fmr);
        spunOut.Covering13FCik.Should().Be(Fmr);
    }

    [Fact]
    public void ConflictingEvidence_LeavesTheHolderOnItsOwn()
    {
        var parent = Form13F(Fmr, 50_000);
        var child = Schedule13G("35368", 9_000, new DateOnly(2026, 8, 1));
        var rollup = ReportsThrough("35368", Fmr);
        rollup.HasConflictingEvidence = true;

        Arrange([parent, child], null, rollup).Should().HaveCount(2);
    }

    [Fact]
    public void AnOwnershipParent_NeverNestsAHolder()
    {
        // A recorded corporate parent states ownership, not that its 13F report holds the shares.
        var parent = Form13F(Fmr, 50_000);
        var child = Schedule13G("35368", 9_000, new DateOnly(2026, 8, 1));

        Arrange([parent, child], null, OwnedBy("35368", Fmr)).Should().HaveCount(2);
    }

    [Fact]
    public void Two13FFilers_NeverNest()
    {
        var parent = Form13F(Fmr, 50_000);
        var child = Form13F("35368", 9_000);

        Arrange([parent, child], null, ReportsThrough("35368", Fmr)).Should().HaveCount(2);
        child.Covering13FCik.Should().Be("35368");
    }

    [Fact]
    public void Standalone13DOlderThanTheQuarter_StaysVisible()
    {
        // A 13D stays in force until amended; only a 13F report that holds the filer supersedes it.
        var activist = Schedule13D("900", 4_000, new DateOnly(2025, 11, 3));

        var visible = StockOwnershipPrecedence.Visible(Arrange([Form13F("100", 10), activist]));

        visible.Should().Contain(activist);
        activist.Has(StockHolderRowFlags.Stale).Should().BeFalse();
    }

    [Fact]
    public void FundSeries_NestUnderTheirAdvisers13FFiler()
    {
        var blackRock = Form13F(BlackRock, 900_000);
        var linked = Fund("S000004310", 40_000, adviserCik: BlackRock);
        var unlinked = Fund("S000009999", 1_000);

        var tops = Arrange([blackRock, linked, unlinked]);

        tops.Should().BeEquivalentTo([blackRock, unlinked]);
        linked.ParentRowKey.Should().Be(blackRock.RowKey);
    }

    [Fact]
    public void NonUsNotification_NestsOnlyUnderThe13FFilerWithItsExactLei()
    {
        const string lei = "549300ABCDEFGHIJKL12";
        var blackRock = Form13F(BlackRock, 900_000).WithLei(lei);
        var matched = NonUsNotification("fca-1", 5.12m, lei.ToLowerInvariant());
        var otherLei = NonUsNotification("fca-2", 3.4m, "529900ZYXWVUTSRQPO98");
        var noLei = NonUsNotification("fca-3", 4.1m);

        var tops = Arrange([blackRock, matched, otherLei, noLei]);

        matched.ParentRowKey.Should().Be(blackRock.RowKey);
        blackRock.Children.Should().ContainSingle().Which.Should().BeSameAs(matched);
        tops.Should().BeEquivalentTo([blackRock, otherLei, noLei]);
    }

    [Fact]
    public void NonUsNotification_AnLeiTwo13FFilersShareNestsUnderNeither()
    {
        const string lei = "549300ABCDEFGHIJKL12";
        var first = Form13F(BlackRock, 900_000).WithLei(lei);
        var second = Form13F(Russell, 10_000).WithLei(lei);
        var notification = NonUsNotification("fca-1", 5.12m, lei);

        var tops = Arrange([first, second, notification]);

        notification.ParentRowKey.Should().BeNull();
        tops.Should().Contain(notification);
    }

    [Fact]
    public void NonUsNotification_APercentageWithoutACountStaysVisible()
    {
        var percentOnly = NonUsNotification("fca-1", 5.12m);
        var belowThreshold = NonUsNotification("fca-2", 0m);

        var visible = StockOwnershipPrecedence.Visible(Arrange([percentOnly, belowThreshold]));

        visible.Should().ContainSingle().Which.Should().BeSameAs(percentOnly);
        belowThreshold.Has(StockHolderRowFlags.Stale).Should().BeTrue();
        StockOwnershipPrecedence.StatesOnlyAPercentage(percentOnly).Should().BeTrue();
    }

    [Fact]
    public void ZeroBalancesAndOldInsiderFilings_AreStaleAndHidden()
    {
        var sold = Schedule13G("200", 0, new DateOnly(2026, 8, 1));
        var former = Insider("300", 5_000, new DateOnly(2024, 6, 1));
        var current = Insider("301", 5_000);

        var visible = StockOwnershipPrecedence.Visible(Arrange([sold, former, current]));

        visible.Should().ContainSingle().Which.Should().BeSameAs(current);
        sold.Has(StockHolderRowFlags.Stale).Should().BeTrue();
        former.Has(StockHolderRowFlags.Stale).Should().BeTrue();
    }

    [Fact]
    public void ChangeNeverCrossesSources_ArrangingKeepsEachLanesOwnChange()
    {
        // The 13F row compares with the filer's previous 13F and the 13G with its previous 13G;
        // nesting one under the other must never recompute either against the other source.
        var form13F = Form13F("100", 1_000);
        form13F.ChangeShares = 100;
        var schedule13G = Schedule13G("100", 1_400, new DateOnly(2026, 8, 5));
        schedule13G.ChangeShares = -50;

        Arrange([form13F, schedule13G]);

        form13F.ChangeShares.Should().Be(100);
        schedule13G.ChangeShares.Should().Be(-50);
    }
}
