using Equibles.Finra.HostedService.Services;
using static Equibles.Finra.HostedService.Services.ListingSymbolStartRule;

namespace Equibles.UnitTests.Finra;

public class ListingSymbolStartRuleTests
{
    private static readonly Guid Own = Guid.NewGuid();
    private static readonly Guid Earlier = Guid.NewGuid();

    private static DateOnly D(string date) => DateOnly.Parse(date);

    // Weekdays from start to end inclusive, the shape of a run of stored daily rows.
    private static IEnumerable<DateOnly> Run(string start, string end)
    {
        for (var date = D(start); date <= D(end); date = date.AddDays(1))
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                yield return date;
    }

    private static List<DateOnly> Rows(params IEnumerable<DateOnly>[] runs) =>
        runs.SelectMany(run => run).OrderBy(date => date).ToList();

    private static SymbolClaim Claim(Guid issuer, string first, string last) =>
        new(issuer, D(first), D(last));

    [Fact]
    public void StartsOn_EarlierHolderStatedTheSymbolOverItsRun_StartsAtTheRunAfterTheGap()
    {
        // A fund that took over a symbol in mid-2025 after its earlier holder renamed in 2022.
        var dates = Rows(Run("2020-01-02", "2022-06-08"), Run("2025-06-26", "2026-11-24"));

        var start = StartsOn(dates, Own, [Claim(Earlier, "2020-01-29", "2022-06-01")]);

        start.Should().Be(D("2025-06-26"));
    }

    [Fact]
    public void StartsOn_ListingsOwnSparseRunsFollowTheEarlierHolder_StartsAtTheFirstOfThem()
    {
        // An over-the-counter trust trades the symbol on scattered days long after the earlier
        // holder moved to another symbol; only the earlier holder's run is not the listing's.
        var dates = Rows(
            Run("2020-01-02", "2023-05-19"),
            [D("2025-11-21")],
            Run("2026-01-26", "2026-01-29"),
            [D("2026-07-16")],
            [D("2026-11-17")]
        );

        var start = StartsOn(dates, Own, [Claim(Earlier, "2020-02-28", "2023-05-11")]);

        start.Should().Be(D("2025-11-21"));
    }

    [Fact]
    public void StartsOn_UnclaimedRunBeforeAClaimedOne_StartsAfterTheClaimedRun()
    {
        // Three blank-check companies in a row used one symbol; only the middle one's cover pages
        // are on file, and the listing's line can only start after it.
        var dates = Rows(
            Run("2021-11-10", "2024-08-15"),
            Run("2025-06-24", "2025-10-29"),
            Run("2026-03-27", "2026-11-23")
        );

        var start = StartsOn(dates, Own, [Claim(Earlier, "2025-05-28", "2025-10-29")]);

        start.Should().Be(D("2026-03-27"));
    }

    [Fact]
    public void StartsOn_HolderFiledWithinTheSlackAfterItsLastTrade_StillAccountsForTheRun()
    {
        var dates = Rows(Run("2020-01-02", "2020-07-02"), Run("2026-05-28", "2026-11-24"));
        // A filing a month after the earlier holder's last stored day.
        var lastFiled = D("2020-07-02").AddDays(StatementSlackDays).ToString("yyyy-MM-dd");

        var start = StartsOn(dates, Own, [Claim(Earlier, lastFiled, lastFiled)]);

        start.Should().Be(D("2026-05-28"));
    }

    [Fact]
    public void StartsOn_ListingsOwnIssuerStatedTheSymbolDuringTheRun_IsNull()
    {
        // The listing's own issuer named the symbol before the run ended, so the run may be its own
        // even though another filer named it too.
        var dates = Rows(Run("2020-01-02", "2022-06-08"), Run("2025-06-26", "2026-11-24"));

        var start = StartsOn(
            dates,
            Own,
            [Claim(Earlier, "2020-01-29", "2022-06-01"), Claim(Own, "2021-03-01", "2026-11-01")]
        );

        start.Should().BeNull();
    }

    [Fact]
    public void StartsOn_OtherIssuersStatementsFallOutsideEveryEarlierRun_IsNull()
    {
        var dates = Rows(Run("2020-01-02", "2021-01-29"), Run("2025-10-31", "2026-11-24"));

        var start = StartsOn(dates, Own, [Claim(Earlier, "2023-01-01", "2024-01-01")]);

        start.Should().BeNull();
    }

    [Fact]
    public void StartsOn_OneUnbrokenRun_IsNullEvenWhenAnotherIssuerClaimsIt()
    {
        // The run the listing trades in now is never handed to another issuer.
        var dates = Rows(Run("2020-01-02", "2026-11-24"));

        var start = StartsOn(dates, Own, [Claim(Earlier, "2020-01-29", "2026-06-01")]);

        start.Should().BeNull();
    }

    [Fact]
    public void StartsOn_GapOfExactlyTheThreshold_DoesNotSplitTheRun()
    {
        var dates = new List<DateOnly> { D("2024-01-01"), D("2024-01-01").AddDays(GapDays) };

        var start = StartsOn(dates, Own, [Claim(Earlier, "2024-01-01", "2024-01-01")]);

        start.Should().BeNull();
    }

    [Fact]
    public void StartsOn_ClaimFiledOnTheEarliestDate_IsReadWithoutOverflow()
    {
        // A filing stored with an unset date sits at the calendar's first day; the slack must not
        // step past it.
        var dates = Rows(Run("2020-01-02", "2020-07-02"), Run("2026-05-28", "2026-11-24"));

        var start = StartsOn(
            dates,
            Own,
            [new SymbolClaim(Earlier, DateOnly.MinValue, DateOnly.MinValue)]
        );

        start.Should().BeNull();
    }

    [Fact]
    public void StartsOn_CoRegistrantStillStatesTheSymbol_IsNull()
    {
        // A partnership and its parent file one cover page naming the partnership's units; the
        // parent states the symbol for as long as the units trade, so it never held it before.
        var dates = Rows(Run("2020-01-02", "2020-01-10"), Run("2020-02-21", "2026-11-24"));

        var start = StartsOn(
            dates,
            Own,
            [Claim(Earlier, "2020-02-19", "2026-11-02"), Claim(Own, "2020-02-19", "2026-11-09")]
        );

        start.Should().BeNull();
    }

    [Fact]
    public void StartsOn_OtherLineWithTheSameSeparatorFreeSymbolStillTrades_IsNull()
    {
        // A REIT's preferred INN-PF reads as INNPF, the over-the-counter symbol of an unrelated
        // company whose own thinly traded rows pause for more than a month.
        var dates = Rows(Run("2025-01-02", "2026-08-14"), Run("2026-10-01", "2026-11-24"));

        var start = StartsOn(dates, Own, [Claim(Earlier, "2021-11-03", "2026-11-03")]);

        start.Should().BeNull();
    }
}
