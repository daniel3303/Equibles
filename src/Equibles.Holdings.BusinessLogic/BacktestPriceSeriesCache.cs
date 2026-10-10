using Equibles.Core.AutoWiring;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Holdings.BusinessLogic;

/// <summary>
/// Process-wide exact-listing close series for the clone backtests. Clone cards, the multi-filer
/// page, the smart-money index and fund scoring read the same three-year windows for the same
/// few thousand listings, so each listing's rows are read once per window start and extended
/// with a bounded delta query instead of being re-read for every holder.
/// </summary>
[Service(ServiceLifetime.Singleton)]
public sealed class BacktestPriceSeriesCache : IDisposable
{
    // Rows, not bytes: about 20 bytes per row keeps the whole US universe over three years
    // (roughly seven million rows) resident with headroom.
    public const long DefaultRowCapacity = 12_000_000;

    // The window start moves daily, so entries for an old start are garbage after a day.
    private static readonly TimeSpan EntryLifetime = TimeSpan.FromHours(36);

    private readonly MemoryCache _cache;

    public BacktestPriceSeriesCache()
        : this(DefaultRowCapacity) { }

    internal BacktestPriceSeriesCache(long rowCapacity)
    {
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = rowCapacity });
    }

    public bool TryGet(Guid listingId, DateOnly windowFrom, out CachedListingSeries series) =>
        _cache.TryGetValue((listingId, windowFrom), out series);

    public void Set(Guid listingId, DateOnly windowFrom, CachedListingSeries series) =>
        _cache.Set(
            (listingId, windowFrom),
            series,
            new MemoryCacheEntryOptions
            {
                Size = Math.Max(1, series.RowCount),
                AbsoluteExpirationRelativeToNow = EntryLifetime,
            }
        );

    public void Remove(Guid listingId, DateOnly windowFrom) =>
        _cache.Remove((listingId, windowFrom));

    public void Dispose() => _cache.Dispose();
}

/// <summary>
/// One listing's cached rows: traded closes above zero, ascending by date, split into runs of
/// the symbol each row was stored under so the loader's per-row ticker matching is unchanged.
/// </summary>
public sealed class CachedListingSeries
{
    /// <summary>When the full window was read; a split captured or applied later evicts it.</summary>
    public required DateTime LoadedAt { get; init; }

    /// <summary>Newest cached date, or null when the window held no rows.</summary>
    public required DateOnly? MaxDate { get; init; }

    public required IReadOnlyList<CachedSeriesSegment> Segments { get; init; }

    public int RowCount => Segments.Sum(segment => segment.Dates.Length);
}

public sealed record CachedSeriesSegment(string SourceTicker, DateOnly[] Dates, decimal[] Closes);
