using Equibles.Holdings.BusinessLogic;

namespace Equibles.UnitTests.Holdings;

public class BacktestPriceLoaderSeriesTests
{
    [Fact]
    public void ToSeries_GroupsAdjacentRowsBySymbolInDateOrder()
    {
        var listingId = Guid.NewGuid();
        var series = BacktestPriceLoader.ToSeries(
            [
                new BacktestPriceLoader.QueriedPriceRow(
                    listingId,
                    "B",
                    new DateOnly(2023, 1, 5),
                    3m
                ),
                new BacktestPriceLoader.QueriedPriceRow(
                    listingId,
                    "A",
                    new DateOnly(2023, 1, 3),
                    1m
                ),
                new BacktestPriceLoader.QueriedPriceRow(
                    listingId,
                    "A",
                    new DateOnly(2023, 1, 4),
                    2m
                ),
            ],
            DateTime.UtcNow
        );

        series.MaxDate.Should().Be(new DateOnly(2023, 1, 5));
        series.RowCount.Should().Be(3);
        series.Segments.Select(segment => segment.SourceTicker).Should().Equal("A", "B");
        series.Segments[0].Closes.Should().Equal(1m, 2m);
    }

    [Fact]
    public void ToSeries_NoRows_IsAnEmptySeriesWithoutADate()
    {
        var series = BacktestPriceLoader.ToSeries([], DateTime.UtcNow);

        series.MaxDate.Should().BeNull();
        series.RowCount.Should().Be(0);
        series.Segments.Should().BeEmpty();
    }

    [Fact]
    public void SeriesCache_RejectsAnEntryOverTheRemainingCapacity()
    {
        using var cache = new BacktestPriceSeriesCache(rowCapacity: 3);
        var windowFrom = new DateOnly(2023, 1, 1);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        cache.Set(first, windowFrom, Series(2));
        cache.Set(second, windowFrom, Series(2));

        cache.TryGet(first, windowFrom, out _).Should().BeTrue();
        cache.TryGet(second, windowFrom, out _).Should().BeFalse("the second entry does not fit");
    }

    [Fact]
    public void TailFrom_StartsTheUnsettledTailBeforeTheLastRowAndNeverBeforeTheWindow()
    {
        var windowFrom = new DateOnly(2023, 1, 1);

        BacktestPriceLoader.TailFrom(null, windowFrom).Should().Be(windowFrom);
        BacktestPriceLoader.TailFrom(windowFrom.AddDays(5), windowFrom).Should().Be(windowFrom);
        BacktestPriceLoader
            .TailFrom(windowFrom.AddDays(40), windowFrom)
            .Should()
            .Be(windowFrom.AddDays(40 - BacktestPriceSeriesCache.UnsettledTailDays));
    }

    [Fact]
    public void TailBuckets_GroupSeriesByTheMonthTheyEndIn_AndReadFromTheEarliestTail()
    {
        var windowFrom = new DateOnly(2022, 1, 3);
        var loadedAt = DateTime.UtcNow;
        var earlyJanuary = (Guid.NewGuid(), Series(new DateOnly(2023, 1, 5), loadedAt));
        var lateJanuary = (Guid.NewGuid(), Series(new DateOnly(2023, 1, 27), loadedAt));
        var february = (Guid.NewGuid(), Series(new DateOnly(2023, 2, 1), loadedAt));
        var empty = (Guid.NewGuid(), BacktestPriceLoader.ToSeries([], loadedAt));

        var buckets = BacktestPriceLoader
            .TailBuckets([earlyJanuary, lateJanuary, february, empty], windowFrom)
            .OrderBy(bucket => bucket.TailFrom)
            .ToList();

        buckets.Should().HaveCount(3, "two January ends share a bucket");
        buckets[0].TailFrom.Should().Be(windowFrom, "a series without rows is re-read whole");
        buckets[0].Hits.Select(hit => hit.ListingId).Should().Equal(empty.Item1);
        buckets[1]
            .TailFrom.Should()
            .Be(
                new DateOnly(2023, 1, 5).AddDays(-BacktestPriceSeriesCache.UnsettledTailDays),
                "the bucket starts at the earliest tail among its listings"
            );
        buckets[1]
            .Hits.Select(hit => hit.ListingId)
            .Should()
            .BeEquivalentTo([earlyJanuary.Item1, lateJanuary.Item1]);
        buckets[2].Hits.Select(hit => hit.ListingId).Should().Equal(february.Item1);
    }

    private static CachedListingSeries Series(DateOnly lastDate, DateTime loadedAt) =>
        BacktestPriceLoader.ToSeries(
            [new BacktestPriceLoader.QueriedPriceRow(Guid.Empty, "X", lastDate, 10m)],
            loadedAt
        );

    [Fact]
    public void Replace_KeepsRowsBeforeTheTailAndTakesTheFreshRowsFromThere()
    {
        var loadedAt = new DateTime(2023, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var series = BacktestPriceLoader.ToSeries(
            [
                new BacktestPriceLoader.QueriedPriceRow(
                    Guid.Empty,
                    "A",
                    new DateOnly(2023, 1, 3),
                    1m
                ),
                new BacktestPriceLoader.QueriedPriceRow(
                    Guid.Empty,
                    "A",
                    new DateOnly(2023, 1, 10),
                    2m
                ),
                new BacktestPriceLoader.QueriedPriceRow(
                    Guid.Empty,
                    "B",
                    new DateOnly(2023, 1, 20),
                    3m
                ),
            ],
            loadedAt
        );

        var replaced = BacktestPriceLoader.Replace(
            series,
            new DateOnly(2023, 1, 10),
            new DateOnly(2023, 1, 31),
            [
                new BacktestPriceLoader.QueriedPriceRow(
                    Guid.Empty,
                    "A",
                    new DateOnly(2023, 1, 10),
                    2.5m
                ),
                new BacktestPriceLoader.QueriedPriceRow(
                    Guid.Empty,
                    "B",
                    new DateOnly(2023, 1, 21),
                    4m
                ),
            ]
        );

        replaced.LoadedAt.Should().Be(loadedAt, "the full read's time still bounds the entry");
        replaced.MaxDate.Should().Be(new DateOnly(2023, 1, 21));
        replaced
            .Segments.SelectMany(segment =>
                segment.Dates.Zip(
                    segment.Closes,
                    (date, close) => (segment.SourceTicker, date, close)
                )
            )
            .Should()
            .Equal(
                ("A", new DateOnly(2023, 1, 3), 1m),
                ("A", new DateOnly(2023, 1, 10), 2.5m),
                ("B", new DateOnly(2023, 1, 21), 4m)
            );
    }

    [Fact]
    public void Replace_ReturnsTheSameSeriesWhenTheTailIsUnchanged()
    {
        var series = BacktestPriceLoader.ToSeries(
            [
                new BacktestPriceLoader.QueriedPriceRow(
                    Guid.Empty,
                    "A",
                    new DateOnly(2023, 1, 3),
                    1m
                ),
                new BacktestPriceLoader.QueriedPriceRow(
                    Guid.Empty,
                    "A",
                    new DateOnly(2023, 1, 20),
                    2m
                ),
            ],
            DateTime.UtcNow
        );
        var listingId = Guid.NewGuid();

        var unchanged = BacktestPriceLoader.Replace(
            series,
            new DateOnly(2023, 1, 10),
            new DateOnly(2023, 1, 31),
            [new BacktestPriceLoader.QueriedPriceRow(listingId, "A", new DateOnly(2023, 1, 20), 2m)]
        );
        var changed = BacktestPriceLoader.Replace(
            series,
            new DateOnly(2023, 1, 10),
            new DateOnly(2023, 1, 31),
            [
                new BacktestPriceLoader.QueriedPriceRow(
                    listingId,
                    "A",
                    new DateOnly(2023, 1, 20),
                    2.5m
                ),
            ]
        );

        unchanged.Should().BeSameAs(series);
        changed.Should().NotBeSameAs(series);
        changed.Segments.Single().Closes.Should().Equal(1m, 2.5m);
    }

    [Fact]
    public void Replace_KeepsRowsAfterAShorterReadsEnd()
    {
        var loadedAt = DateTime.UtcNow;
        var series = BacktestPriceLoader.ToSeries(
            [
                new BacktestPriceLoader.QueriedPriceRow(
                    Guid.Empty,
                    "A",
                    new DateOnly(2023, 1, 3),
                    1m
                ),
                new BacktestPriceLoader.QueriedPriceRow(
                    Guid.Empty,
                    "A",
                    new DateOnly(2023, 1, 20),
                    2m
                ),
                new BacktestPriceLoader.QueriedPriceRow(
                    Guid.Empty,
                    "A",
                    new DateOnly(2023, 2, 3),
                    3m
                ),
            ],
            loadedAt
        );

        var replaced = BacktestPriceLoader.Replace(
            series,
            new DateOnly(2023, 1, 14),
            new DateOnly(2023, 1, 25),
            [
                new BacktestPriceLoader.QueriedPriceRow(
                    Guid.Empty,
                    "A",
                    new DateOnly(2023, 1, 20),
                    2.5m
                ),
            ]
        );

        replaced.MaxDate.Should().Be(new DateOnly(2023, 2, 3));
        replaced.Segments.Single().Closes.Should().Equal(1m, 2.5m, 3m);
    }

    [Fact]
    public void SeriesCache_ExpiresFromTheFullReadNotFromTheLastRefresh()
    {
        using var cache = new BacktestPriceSeriesCache(rowCapacity: 100);
        var windowFrom = new DateOnly(2023, 1, 1);
        var listingId = Guid.NewGuid();
        var stale = new CachedListingSeries
        {
            LoadedAt =
                DateTime.UtcNow - BacktestPriceSeriesCache.EntryLifetime - TimeSpan.FromMinutes(1),
            MaxDate = windowFrom,
            Segments = [new CachedSeriesSegment("T", [windowFrom], [1m])],
        };

        cache.Set(listingId, windowFrom, stale);

        cache
            .TryGet(listingId, windowFrom, out _)
            .Should()
            .BeFalse("the entry is already past its lifetime");
    }

    private static CachedListingSeries Series(int rows) =>
        new()
        {
            LoadedAt = DateTime.UtcNow,
            MaxDate = new DateOnly(2023, 1, rows),
            Segments =
            [
                new CachedSeriesSegment(
                    "T",
                    Enumerable.Range(1, rows).Select(day => new DateOnly(2023, 1, day)).ToArray(),
                    Enumerable.Range(1, rows).Select(day => (decimal)day).ToArray()
                ),
            ],
        };
}
