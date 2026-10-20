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
        // Every listing was last asked before its market's newest settled session closed.
        var large = Enumerable
            .Range(0, 1000)
            .Select(index =>
                Target("LARGE" + index) with
                {
                    YahooPriceSyncAttemptedAt = Now.AddHours(-28),
                }
            )
            .ToArray();
        var smaller = Target("SMALL") with
        {
            MarketCountryCode = "DK",
            MarketIdentifierCode = "XCSE",
            YahooPriceSyncAttemptedAt = Now.AddHours(-26),
        };
        var other = Target("OTHER") with
        {
            MarketCountryCode = "PL",
            MarketIdentifierCode = "XWAR",
            YahooPriceSyncAttemptedAt = Now.AddHours(-27),
        };
        var targets = large.Append(smaller).Append(other).ToArray();
        var dates = targets.ToDictionary(
            target => target.EquityListingId,
            _ => hasRecentPrices ? (DateOnly?)new(2026, 9, 28) : null
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

    [Fact]
    public void RetriedThinNamesCannotHoldBackAnotherMarketsUnaskedSession()
    {
        // Hong Kong's 2026-09-30 session settled at 08:40 UTC and nobody has asked for it yet.
        var hongKong = Enumerable
            .Range(0, 300)
            .Select(index =>
                VenueTarget((index + 1).ToString("D5"), "HK", "XHKG") with
                {
                    YahooPriceSyncAttemptedAt = Now.AddHours(-16),
                }
            )
            .ToArray();
        // Paris thin names still lack the 2026-09-29 close after being asked for it this morning.
        var parisRetries = Enumerable
            .Range(0, 40)
            .Select(index =>
                VenueTarget("FR" + index, "FR", "XPAR") with
                {
                    YahooPriceSyncAttemptedAt = Now.AddMinutes(-30),
                }
            )
            .ToArray();
        var targets = parisRetries.Concat(hongKong).ToArray();
        var dates = targets.ToDictionary(
            target => target.EquityListingId,
            target => hongKong.Contains(target) ? (DateOnly?)new(2026, 9, 29) : new(2026, 9, 28)
        );

        var hongKongIds = hongKong.Select(target => target.EquityListingId).ToHashSet();

        var ordered = YahooCatalogPriceQueue.Order(targets, dates, Now);

        ordered.Should().HaveCount(targets.Length);
        ordered
            .Take(hongKong.Length)
            .Should()
            .OnlyContain(target => hongKongIds.Contains(target.EquityListingId));
    }

    [Fact]
    public void AWeekendGapLeavesTheSeriesCurrent()
    {
        var saturday = new DateTime(2026, 10, 3, 18, 0, 0, DateTimeKind.Utc);
        // FRIDAY holds Friday's close; THURSDAY was re-asked for it this afternoon and still lacks it.
        var friday = VenueTarget("FRIDAY", "GB", "XLON") with
        {
            YahooPriceSyncAttemptedAt = saturday.AddDays(-1).AddHours(-1),
        };
        var thursday = friday with
        {
            EquityListingId = Guid.NewGuid(),
            Ticker = "THURSDAY",
            YahooPriceSyncAttemptedAt = saturday.AddHours(-2),
        };
        var dates = new Dictionary<Guid, DateOnly?>
        {
            [friday.EquityListingId] = new(2026, 10, 2),
            [thursday.EquityListingId] = new(2026, 10, 1),
        };

        YahooCatalogPriceQueue
            .Order([friday, thursday], dates, saturday)
            .Should()
            .Equal(thursday, friday);
    }

    // A security id makes the target a catalog listing, so the venue's own clock settles its sessions.
    private static PriceSeriesTarget VenueTarget(string ticker, string country, string mic) =>
        Target(ticker) with
        {
            MarketCountryCode = country,
            MarketIdentifierCode = mic,
            EquitySecurityId = Guid.NewGuid(),
        };

    private static PriceSeriesTarget Target(string ticker) =>
        new(
            ticker,
            Guid.NewGuid(),
            Guid.NewGuid(),
            true,
            MarketCountryCode: "HK",
            MarketIdentifierCode: "XHKG"
        );

    [Fact]
    public void MissingClosePrecedesAlreadyCurrentSymbolsAcrossRestarts()
    {
        var current = Enumerable
            .Range(0, 30)
            .Select(index =>
                Target("CURRENT" + index) with
                {
                    MarketCountryCode = "DE",
                    MarketIdentifierCode = "XETR",
                    EquitySecurityId = Guid.NewGuid(),
                    YahooPriceSyncAttemptedAt = Now.AddDays(-1),
                }
            )
            .ToArray();
        var missing = current[0] with
        {
            EquityListingId = Guid.NewGuid(),
            Ticker = "MISSING",
            YahooPriceSyncAttemptedAt = Now.AddHours(-1),
        };
        var dates = current.ToDictionary(
            target => target.EquityListingId,
            _ => (DateOnly?)new(2026, 9, 29)
        );
        dates[missing.EquityListingId] = new(2026, 9, 28);
        var targets = current.Append(missing).ToArray();

        YahooCatalogPriceQueue.Order(targets, dates, Now).First().Should().Be(missing);
        YahooCatalogPriceQueue
            .Order(targets, dates, Now.AddMinutes(5))
            .First()
            .Should()
            .Be(missing);
        dates[missing.EquityListingId] = new(2026, 9, 29);
        current.Should().Contain(YahooCatalogPriceQueue.Order(targets, dates, Now).First());
    }
}
