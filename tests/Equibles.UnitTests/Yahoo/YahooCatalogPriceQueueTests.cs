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
