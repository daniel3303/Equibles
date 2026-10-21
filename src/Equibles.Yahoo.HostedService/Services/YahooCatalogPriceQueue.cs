namespace Equibles.Yahoo.HostedService.Services;

internal static class YahooCatalogPriceQueue
{
    private const int CurrentPerBackfill = 4;

    public static List<PriceSeriesTarget> Order(
        IReadOnlyCollection<PriceSeriesTarget> targets,
        IReadOnlyDictionary<Guid, DateOnly?> lastDates,
        DateTime now,
        IReadOnlySet<Guid> venueCovered = null,
        TimeSpan coveredFetchInterval = default
    )
    {
        var activeSince = DateOnly.FromDateTime(now).AddDays(-10);
        bool Active(PriceSeriesTarget target) =>
            lastDates.GetValueOrDefault(target.EquityListingId) >= activeSince;
        // Persisted attempts rotate the unfinished tail across restarts. Unsupported symbols
        // retry hourly, while recently trading series can discover a newly published bar in five minutes.
        var due = targets
            .Where(target =>
                target.YahooPriceSyncAttemptedAt == null
                || target.YahooPriceSyncAttemptedAt
                    <= now.Subtract(
                        Active(target) ? TimeSpan.FromMinutes(5) : TimeSpan.FromHours(1)
                    )
            )
            .ToArray();
        // A session nobody has asked for yet outranks a retry: rotating every market evenly let small
        // markets' re-asked thin names hold Shanghai and Hong Kong to a few hundred closes a day.
        var current = new Queue<PriceSeriesTarget>(
            due.Where(Active)
                .OrderBy(target => target.YahooPriceSyncAttemptedAt ?? DateTime.MinValue)
                .ThenBy(target => lastDates.GetValueOrDefault(target.EquityListingId))
                .ThenBy(target => target.EquityListingId)
                .GroupBy(target =>
                    Priority(target, lastDates, now, venueCovered, coveredFetchInterval)
                )
                .OrderBy(tier => tier.Key)
                .SelectMany(RotateMarkets)
        );
        var backfill = new Queue<PriceSeriesTarget>(
            RotateMarkets(
                due.Where(target => !Active(target))
                    .OrderBy(target => target.YahooPriceSyncAttemptedAt ?? DateTime.MinValue)
                    .ThenBy(target => target.EquityListingId)
            )
        );
        var result = new List<PriceSeriesTarget>(due.Length);
        while (current.Count > 0 || backfill.Count > 0)
        {
            for (
                var index = 0;
                index < CurrentPerBackfill && current.TryDequeue(out var target);
                index++
            )
                result.Add(target);
            if (backfill.TryDequeue(out var next))
                result.Add(next);
        }
        return result;
    }

    private enum SessionPriority
    {
        Unasked,
        Retry,
        Current,
    }

    private static SessionPriority Priority(
        PriceSeriesTarget target,
        IReadOnlyDictionary<Guid, DateOnly?> lastDates,
        DateTime now,
        IReadOnlySet<Guid> venueCovered,
        TimeSpan coveredFetchInterval
    )
    {
        var settledBefore = YahooListingSource.SettledBefore(target, now);
        var start = lastDates.GetValueOrDefault(target.EquityListingId)?.AddDays(1);
        if (start is not { } from || !YahooListingSource.HasWeekdaySession(from, settledBefore))
            // A venue keeps the series current, but the feed still owes its interval fetch (and a
            // first history), which must not wait behind every market's retries.
            return
                venueCovered?.Contains(target.EquityListingId) == true
                && (
                    target.YahooPriceSyncAttemptedAt == null
                    || target.YahooPriceSyncAttemptedAt <= now - coveredFetchInterval
                )
                ? SessionPriority.Unasked
                : SessionPriority.Current;
        return target.YahooPriceSyncAttemptedAt >= YahooListingSource.SettledSince(target, now)
            ? SessionPriority.Retry
            : SessionPriority.Unasked;
    }

    private static IEnumerable<PriceSeriesTarget> RotateMarkets(
        IEnumerable<PriceSeriesTarget> ordered
    )
    {
        // A large newly admitted venue must not consume every bounded batch before other markets.
        var markets = new Queue<Queue<PriceSeriesTarget>>(
            ordered
                .GroupBy(target => (target.MarketCountryCode, target.MarketIdentifierCode))
                .Select(group => new Queue<PriceSeriesTarget>(group))
        );
        while (markets.TryDequeue(out var market))
        {
            yield return market.Dequeue();
            if (market.Count > 0)
                markets.Enqueue(market);
        }
    }
}
