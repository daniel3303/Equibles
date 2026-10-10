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
    public void SeriesCache_EvictsWhenTheRowCapacityIsExceeded()
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
