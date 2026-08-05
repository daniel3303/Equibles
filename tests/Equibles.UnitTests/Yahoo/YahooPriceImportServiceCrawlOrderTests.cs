using Equibles.Yahoo.HostedService.Services;

namespace Equibles.UnitTests.Yahoo;

/// <summary>
/// Tests for the crawl priority rule in <see cref="YahooPriceImportService"/>.
///
/// The lane used to sort the whole universe stalest-first, which reads as obviously correct and was
/// the reason the daily price lane fell days behind. Stocks that will never return data again —
/// delisted tickers, bankruptcy-suffixed symbols, expired warrants, foreign OTC lines Yahoo does not
/// serve — sort as "stalest" precisely because they have no recent data, so they led every single
/// cycle. In production 617 of them sat ahead of the 5,484 stocks that were merely missing the
/// previous session's bar, and a worker restarting more often than a full pass takes never got past
/// them: the site showed a close from two sessions earlier while the lane worked flat out.
/// </summary>
public class YahooPriceImportServiceCrawlOrderTests
{
    private static readonly DateOnly Today = new(2026, 7, 27);

    private static List<string> Order(
        Dictionary<string, Guid> tickerMap,
        Dictionary<Guid, DateOnly> lastDates
    )
    {
        var targets = tickerMap
            .Select(pair => new PriceSeriesTarget(pair.Key, pair.Value, IsPrimary: true))
            .ToList();
        var seriesDates = tickerMap
            .Where(pair => lastDates.ContainsKey(pair.Value))
            .ToDictionary(
                pair => new PriceSeriesKey(pair.Value, pair.Key),
                pair => lastDates[pair.Value]
            );

        return YahooPriceImportService
            .BuildCrawlOrder(targets, seriesDates, Today)
            .Select(target => target.Ticker)
            .ToList();
    }

    [Fact]
    public void ActivelyTradedStocks_LeadLongDormantOnes()
    {
        // The defect in one assertion: DEAD is "stalest" and would lead under the old rule, pushing
        // the stock that is merely one session behind to the back of an hours-long crawl.
        var live = Guid.NewGuid();
        var dead = Guid.NewGuid();
        var tickerMap = new Dictionary<string, Guid> { ["DEAD"] = dead, ["LIVE"] = live };
        var lastDates = new Dictionary<Guid, DateOnly>
        {
            [dead] = new DateOnly(2026, 5, 11),
            [live] = new DateOnly(2026, 7, 23),
        };

        Order(tickerMap, lastDates).Should().Equal("LIVE", "DEAD");
    }

    [Fact]
    public void NeverSyncedStocks_GoToTheTail_NotTheHead()
    {
        // A stock with no rows at all has no date to sort on, so it used to lead the crawl. Its
        // backfill matters, but never at the cost of the whole universe's daily freshness — and it
        // still runs every cycle, just after the working set.
        var fresh = Guid.NewGuid();
        var neverSynced = Guid.NewGuid();
        var tickerMap = new Dictionary<string, Guid> { ["NEW"] = neverSynced, ["CUR"] = fresh };
        var lastDates = new Dictionary<Guid, DateOnly> { [fresh] = new DateOnly(2026, 7, 24) };

        Order(tickerMap, lastDates).Should().Equal("CUR", "NEW");
    }

    [Fact]
    public void WithinTheActiveGroup_TheStalestStillLeads()
    {
        // The original ordering intent is preserved where it actually helps: among stocks that are
        // genuinely trading, the one furthest behind is caught up first, so a partial cycle spends
        // itself on the biggest gaps rather than re-syncing already-current stocks.
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var tickerMap = new Dictionary<string, Guid>
        {
            ["CUR"] = c,
            ["MID"] = b,
            ["OLD"] = a,
        };
        var lastDates = new Dictionary<Guid, DateOnly>
        {
            [a] = new DateOnly(2026, 7, 21),
            [b] = new DateOnly(2026, 7, 23),
            [c] = new DateOnly(2026, 7, 24),
        };

        Order(tickerMap, lastDates).Should().Equal("OLD", "MID", "CUR");
    }

    [Fact]
    public void TheActiveWindow_ClearsALongWeekendPlusAHoliday()
    {
        // A healthy stock must not drop out of the working set just because the market was shut for
        // several days — that would demote it exactly when it is about to need a fresh bar. Four
        // calendar days back is still comfortably active.
        var holidayGap = Guid.NewGuid();
        var dormant = Guid.NewGuid();
        var tickerMap = new Dictionary<string, Guid>
        {
            ["DORMANT"] = dormant,
            ["HOLIDAY"] = holidayGap,
        };
        var lastDates = new Dictionary<Guid, DateOnly>
        {
            [dormant] = new DateOnly(2026, 6, 15),
            [holidayGap] = Today.AddDays(-4),
        };

        Order(tickerMap, lastDates).Should().Equal("HOLIDAY", "DORMANT");
    }

    [Fact]
    public void EveryStockStillAppearsExactlyOnce()
    {
        // Partitioning must reorder the crawl, never shrink it — dropping a stock would silently
        // freeze its price series with nothing in the logs to show for it.
        var ids = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToList();
        var tickerMap = ids.Select((id, i) => (id, i)).ToDictionary(x => $"T{x.i}", x => x.id);
        var lastDates = new Dictionary<Guid, DateOnly>
        {
            [ids[0]] = new DateOnly(2026, 7, 24),
            [ids[1]] = new DateOnly(2026, 1, 2),
            [ids[3]] = new DateOnly(2026, 7, 20),
        };

        Order(tickerMap, lastDates).Should().BeEquivalentTo(tickerMap.Keys);
    }

    [Fact]
    public void SecondaryListing_FreshnessIsIndependentFromThePrimary()
    {
        var stockId = Guid.NewGuid();
        var primary = new PriceSeriesTarget("GOOGL", stockId, IsPrimary: true);
        var secondary = new PriceSeriesTarget("GOOG", stockId, IsPrimary: false);
        var lastDates = new Dictionary<PriceSeriesKey, DateOnly>
        {
            [primary.Key] = Today.AddDays(-1),
            [secondary.Key] = Today.AddDays(-30),
        };

        var ordered = YahooPriceImportService.BuildCrawlOrder(
            [secondary, primary],
            lastDates,
            Today
        );

        ordered.Select(target => target.Ticker).Should().Equal("GOOGL", "GOOG");
    }
}
