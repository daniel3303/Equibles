using Equibles.Holdings.BusinessLogic.Ownership;
using Equibles.Holdings.BusinessLogic.Ownership.Models;
using Equibles.Holdings.Data.Models;
using static Equibles.UnitTests.Holdings.Ownership.OwnershipRows;

namespace Equibles.UnitTests.Holdings.Ownership;

public class OwnershipSummaryTests
{
    private static StockOwnershipSummary Summarize(
        IReadOnlyList<StockHolderRow> rows,
        long? sharesOutstanding = 10_000,
        string insidersNotSummed = null,
        params HolderRollup[] rollups
    )
    {
        StockOwnershipPrecedence.Arrange(
            rows,
            new StockOwnershipPrecedence.Context
            {
                Rollups = rollups.ToDictionary(rollup => rollup.Cik),
                Today = Today,
            }
        );
        return OwnershipSummaryCalculator.Build(
            rows,
            new OwnershipSummaryCalculator.Context
            {
                SharesOutstanding = sharesOutstanding,
                InsidersNotSummedReason = insidersNotSummed,
            }
        );
    }

    [Fact]
    public void FundsNeverSummed()
    {
        var adviser = Form13F("100", 3_000);
        var fund = Fund("S000000001", 2_500, adviserCik: "100");
        var standalone = Fund("S000000002", 400);

        var summary = Summarize([adviser, fund, standalone]);

        summary.Institutional.Shares.Should().Be(3_000);
        summary.Funds.Shares.Should().Be(2_900);
        summary.Funds.Summed.Should().BeFalse();
        summary.Remaining.Shares.Should().Be(7_000);
        fund.Has(StockHolderRowFlags.NotSummed).Should().BeTrue();
        standalone.Has(StockHolderRowFlags.NotSummed).Should().BeTrue();
    }

    [Fact]
    public void Insider13FFilerExcluded_AnInsiderWhoFiles13FsCountsOnceAsInstitutional()
    {
        var filer = Form13F("100", 3_000);
        var sameInsider = Insider("100", 2_000);
        var insider = Insider("200", 500);

        var summary = Summarize([filer, sameInsider, insider]);

        summary.Institutional.Shares.Should().Be(3_000);
        summary.Insiders.Shares.Should().Be(500);
        summary.Insiders.Holders.Should().Be(1);
        sameInsider.Has(StockHolderRowFlags.NotSummed).Should().BeTrue();
        summary.Remaining.Shares.Should().Be(6_500);
    }

    [Fact]
    public void Insider13FFilerExcluded_ContainedInsidersAndCoPersonsOf13FFilersToo()
    {
        var parent = Form13F("100", 3_000);
        var contained = Insider("150", 700);
        // A 13D the 13F filer files names an individual co-person who is also an insider.
        var group13D = Schedule13D("100", 3_100, new DateOnly(2026, 8, 1), "100", "160");
        var coPerson = Insider("160", 900);

        var summary = Summarize(
            [parent, contained, group13D, coPerson],
            rollups: ReportsThrough("150", "100")
        );

        summary.Insiders.Shares.Should().Be(0);
        summary.Schedule13DGOnly.Shares.Should().Be(0);
        contained.Covering13FCik.Should().Be("100");
    }

    [Fact]
    public void MultiClassInsidersExcluded()
    {
        var insider = Insider("200", 500);

        var summary = Summarize(
            [Form13F("100", 3_000), insider],
            insidersNotSummed: "more than one class"
        );

        summary.Insiders.Shares.Should().Be(0);
        summary.Insiders.Summed.Should().BeFalse();
        summary.InsidersNotSummedReason.Should().Be("more than one class");
        insider.Has(StockHolderRowFlags.NotSummed).Should().BeTrue();
        summary.Remaining.Shares.Should().Be(7_000);
    }

    [Fact]
    public void NegativeRemainingFlagged_NotClipped()
    {
        var summary = Summarize([
            Form13F("100", 9_000),
            Schedule13G("900", 2_000, new DateOnly(2026, 8, 1)),
        ]);

        summary.Remaining.Shares.Should().Be(-1_000);
        summary.RemainingNegative.Should().BeTrue();
        summary.Remaining.PercentOutstanding.Should().Be(-10m);
    }

    [Fact]
    public void Schedule13DGOnly_CountsUncontainedBlocksOncePerGroup()
    {
        // Two filings naming one another's filers report the same block, so it counts once.
        var lead = Schedule13D("900", 1_200, new DateOnly(2026, 8, 1), "900", "901");
        var coFiler = Schedule13D("901", 1_150, new DateOnly(2026, 8, 2), "901", "900");
        var other = Schedule13G("950", 300, new DateOnly(2026, 7, 15));
        var containedG = Schedule13G("35368", 800, new DateOnly(2026, 8, 3));

        var summary = Summarize(
            [Form13F("315066", 5_000), lead, coFiler, other, containedG],
            rollups: ReportsThrough("35368", "315066")
        );

        summary.Schedule13DGOnly.Shares.Should().Be(1_500);
        summary.Schedule13DGOnly.Holders.Should().Be(2);
        coFiler.Has(StockHolderRowFlags.NotSummed).Should().BeTrue();
        containedG.Has(StockHolderRowFlags.NotSummed).Should().BeTrue();
    }

    [Fact]
    public void Schedule13DGOnly_ABlockFiledByACountedInsiderCountsAsInsider()
    {
        var insider = Insider("400", 2_000);
        var insiderBlock = Schedule13D("400", 2_100, new DateOnly(2026, 8, 1));

        var summary = Summarize([insider, insiderBlock]);

        summary.Insiders.Shares.Should().Be(2_000);
        summary.Schedule13DGOnly.Shares.Should().Be(0);
    }

    [Fact]
    public void Schedule13DGOnly_BlockNamingA13FFilerNotCounted()
    {
        // A 13D naming a 13F filer as a reporting person reports shares that 13F already counts.
        var filer = Form13F("100", 3_000);
        var group13D = Schedule13D("900", 3_200, new DateOnly(2026, 8, 1), "900", "100");
        // A block naming a person of that 13D is the same group, however far the chain runs.
        var chained = Schedule13D("950", 3_150, new DateOnly(2026, 8, 2), "950", "900");

        var summary = Summarize([filer, group13D, chained]);

        summary.Institutional.Shares.Should().Be(3_000);
        summary.Schedule13DGOnly.Shares.Should().Be(0);
        summary.Schedule13DGOnly.Holders.Should().Be(0);
        summary.Remaining.Shares.Should().Be(7_000);
        group13D.Has(StockHolderRowFlags.NotSummed).Should().BeTrue();
        chained.Has(StockHolderRowFlags.NotSummed).Should().BeTrue();
    }

    [Fact]
    public void Insider_FilingA13DNamingA13FFiler_Excluded()
    {
        // The insider and the 13F filer report one group's shares, which the 13F already counts.
        var filer = Form13F("100", 3_000);
        var insider = Insider("400", 2_000);
        var group13D = Schedule13D("400", 3_100, new DateOnly(2026, 8, 1), "400", "100");

        var summary = Summarize([filer, insider, group13D]);

        summary.Insiders.Shares.Should().Be(0);
        summary.Insiders.Holders.Should().Be(0);
        summary.Schedule13DGOnly.Shares.Should().Be(0);
        summary.Remaining.Shares.Should().Be(7_000);
        insider.Has(StockHolderRowFlags.NotSummed).Should().BeTrue();
        group13D.Has(StockHolderRowFlags.NotSummed).Should().BeTrue();
    }

    [Fact]
    public void Schedule13DGOnly_MutuallyNamingFilingsWhereOnlyTheLaterNamesA13FFiler_NeitherCounted()
    {
        // The first filing names its co-filer and an individual; only the co-filer's names the 13F
        // filer, so a single pass in row order would never reach the individual the first names.
        var lead = Schedule13D("900", 1_200, new DateOnly(2026, 8, 1), "900", "901", "902");
        var coFiler = Schedule13D("901", 1_150, new DateOnly(2026, 8, 2), "901", "900", "100");
        var insider = Insider("902", 1_000);

        var summary = Summarize([lead, coFiler, insider, Form13F("100", 3_000)]);

        summary.Schedule13DGOnly.Shares.Should().Be(0);
        summary.Insiders.Shares.Should().Be(0);
        lead.Has(StockHolderRowFlags.NotSummed).Should().BeTrue();
        coFiler.Has(StockHolderRowFlags.NotSummed).Should().BeTrue();
        insider.Has(StockHolderRowFlags.NotSummed).Should().BeTrue();
        OwnershipSummaryCalculator
            .Covered([lead, coFiler, insider])
            .Should()
            .BeEmpty("no 13F filer is among these rows");
    }

    [Fact]
    public void Insider_NamedOnlyByAnExited13DWithA13FFiler_StillCounted()
    {
        // The group's exit filing reports no shares, so it no longer ties the insider to the filer.
        var filer = Form13F("100", 3_000);
        var insider = Insider("400", 2_000);
        var exited = Schedule13D("100", 0, new DateOnly(2026, 8, 1), "100", "400");

        var summary = Summarize([filer, insider, exited]);

        summary.Insiders.Shares.Should().Be(2_000);
        insider.Has(StockHolderRowFlags.NotSummed).Should().BeFalse();
    }

    [Fact]
    public void PercentUnavailable_LeavesSharesAndDropsPercent()
    {
        // A foreign private issuer's count is in ordinary shares, a different unit from the 13F
        // rows, so neither a percent nor a remainder may be taken from it.
        var summary = OwnershipSummaryCalculator.Build(
            [Form13F("100", 3_000)],
            new OwnershipSummaryCalculator.Context
            {
                SharesOutstanding = 10_000,
                PercentUnavailableReason = "foreign private issuer",
            }
        );

        summary.Institutional.Shares.Should().Be(3_000);
        summary.Institutional.PercentOutstanding.Should().BeNull();
        summary.Remaining.Should().BeNull();
        summary.SharesOutstanding.Should().BeNull();
    }
}
