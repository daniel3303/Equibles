using Equibles.Yahoo.HostedService.Services;

namespace Equibles.UnitTests.Yahoo;

public class YahooCatalogPriceQueueTests
{
    private static readonly DateTime Now = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NewListingsReceiveCapacityWhileCurrentPricesRemainFirst()
    {
        var current = Enumerable.Range(0, 12).Select(index => Target("CURRENT" + index)).ToArray();
        var newListings = Enumerable.Range(0, 3).Select(index => Target("NEW" + index)).ToArray();
        var dates = current.ToDictionary(
            target => target.EquityListingId,
            _ => (DateOnly?)new(2026, 9, 29)
        );
        var ordered = YahooCatalogPriceQueue.Order(
            newListings.Concat(current).ToArray(),
            dates,
            Now
        );
        ordered.Take(4).Should().OnlyContain(target => current.Contains(target));
        newListings.Should().Contain(ordered[4]);
        newListings.Should().Contain(ordered[9]);
        ordered.Should().BeEquivalentTo(current.Concat(newListings));
    }

    [Fact]
    public void RestartContinuesUntouchedListingsAndRespectsDifferentRetryCadences()
    {
        var untouched = Target("NEW");
        var failed = Target("FAILED") with { YahooPriceSyncAttemptedAt = Now.AddMinutes(-59) };
        var retry = Target("RETRY") with { YahooPriceSyncAttemptedAt = Now.AddHours(-2) };
        var current = Target("CURRENT") with { YahooPriceSyncAttemptedAt = Now.AddMinutes(-5) };
        var recent = Target("RECENT") with { YahooPriceSyncAttemptedAt = Now.AddMinutes(-4) };
        var dates = new Dictionary<Guid, DateOnly?>
        {
            [current.EquityListingId] = new(2026, 9, 29),
            [recent.EquityListingId] = new(2026, 9, 29),
        };
        YahooCatalogPriceQueue
            .Order([failed, retry, untouched, recent, current], dates, Now)
            .Should()
            .Equal(current, untouched, retry);
        YahooCatalogPriceQueue
            .Order([failed, retry, untouched, recent, current], dates, Now.AddMinutes(1))
            .Should()
            .Contain(failed)
            .And.Contain(recent);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LargeVenueCannotFillTheBatchBeforeOtherMarkets(bool hasRecentPrices)
    {
        var large = Enumerable
            .Range(0, 1000)
            .Select(index =>
                Target("LARGE" + index) with
                {
                    YahooPriceSyncAttemptedAt = Now.AddHours(-4),
                }
            )
            .ToArray();
        var smaller = Target("SMALL") with
        {
            MarketCountryCode = "DK",
            MarketIdentifierCode = "XCSE",
            YahooPriceSyncAttemptedAt = Now.AddHours(-2),
        };
        var other = Target("OTHER") with
        {
            MarketCountryCode = "PL",
            MarketIdentifierCode = "XWAR",
            YahooPriceSyncAttemptedAt = Now.AddHours(-3),
        };
        var targets = large.Append(smaller).Append(other).ToArray();
        var dates = targets.ToDictionary(
            target => target.EquityListingId,
            _ => hasRecentPrices ? (DateOnly?)new(2026, 9, 29) : null
        );
        var ordered = YahooCatalogPriceQueue.Order(targets, dates, Now);

        large.Should().Contain(ordered[0]);
        ordered[1].Should().Be(other);
        ordered[2].Should().Be(smaller);
        ordered.Should().BeEquivalentTo(targets);
        ordered.Select(target => target.EquityListingId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void RestartPreservesOldestAttemptFirstInsideEachMarket()
    {
        var older = Target("OLDER") with { YahooPriceSyncAttemptedAt = Now.AddHours(-3) };
        var newer = Target("NEWER") with { YahooPriceSyncAttemptedAt = Now.AddHours(-2) };
        var justServed = Target("SERVED") with { YahooPriceSyncAttemptedAt = Now };
        var other = Target("OTHER") with
        {
            MarketCountryCode = "DK",
            MarketIdentifierCode = "XCSE",
            YahooPriceSyncAttemptedAt = Now.AddHours(-1),
        };
        var targets = new[] { newer, justServed, other, older };
        var dates = targets.ToDictionary(
            target => target.EquityListingId,
            _ => (DateOnly?)new(2026, 9, 29)
        );

        YahooCatalogPriceQueue.Order(targets, dates, Now).Should().Equal(older, other, newer);
        targets = targets
            .Select(target =>
                target == older ? target with { YahooPriceSyncAttemptedAt = Now } : target
            )
            .ToArray();
        YahooCatalogPriceQueue.Order(targets, dates, Now).Should().Equal(newer, other);
    }

    private static PriceSeriesTarget Target(string ticker) =>
        new(
            ticker,
            Guid.NewGuid(),
            Guid.NewGuid(),
            true,
            MarketCountryCode: "HK",
            MarketIdentifierCode: "XHKG"
        );
}
