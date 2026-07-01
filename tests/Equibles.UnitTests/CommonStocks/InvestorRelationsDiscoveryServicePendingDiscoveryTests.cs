using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.HostedService.Services;

namespace Equibles.UnitTests.CommonStocks;

public class InvestorRelationsDiscoveryServicePendingDiscoveryTests
{
    private static readonly DateTime Now = new(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);
    private const int CurrentVersion = 5;

    private static DateTime Utc(string value) => DateTime.Parse(value).ToUniversalTime();

    [Theory]
    [InlineData(null, true)] // probed, but no backoff stamped (legacy row) → re-probe once
    [InlineData("2026-06-10", true)] // backoff elapsed (RetryAfter in the past) → eligible
    [InlineData("2026-06-20", false)] // still backing off (RetryAfter in the future) → skipped
    public void PendingDiscovery_RetryAfter_GatesOnExponentialBackoff(
        string retryAfter,
        bool expected
    )
    {
        // Contract: a definitive miss backs off on an exponential schedule — once RetryAfter has
        // elapsed (or was never stamped) the stock is re-probed; while it's still in the future the
        // stock is skipped. Version is current and the website wasn't found after the probe, so only
        // the backoff clause is in play.
        var stock = new CommonStock
        {
            Website = "https://acme.com",
            InvestorRelationsCheckedAt = Utc("2026-06-09"),
            InvestorRelationsRetryAfter = retryAfter == null ? null : Utc(retryAfter),
            WebsiteCheckedAt = Utc("2026-06-09"),
            InvestorRelationsDiscoveryVersion = CurrentVersion,
        };

        var eligible = InvestorRelationsDiscoveryService
            .PendingDiscovery(Now, CurrentVersion)
            .Compile();

        eligible(stock).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, null, false)] // no website → nothing to probe
    [InlineData("", null, false)] // empty website → nothing to probe
    [InlineData("https://acme.com", "https://ir.acme.com", false)] // already discovered
    public void PendingDiscovery_WebsiteAndIrUrl_ExcludeNonCandidates(
        string website,
        string irUrl,
        bool expected
    )
    {
        var stock = new CommonStock
        {
            Website = website,
            InvestorRelationsUrl = irUrl,
            InvestorRelationsDiscoveryVersion = CurrentVersion,
        };

        var eligible = InvestorRelationsDiscoveryService
            .PendingDiscovery(Now, CurrentVersion)
            .Compile();

        eligible(stock).Should().Be(expected);
    }

    [Theory]
    [InlineData(CurrentVersion - 1, true)] // probed under an older generation → re-sweep now
    [InlineData(CurrentVersion, false)] // already at the current generation → wait out the backoff
    public void PendingDiscovery_OlderVersion_IsEligibleWhileBackingOff(int version, bool expected)
    {
        // Contract: a probe-logic improvement (version bump) re-opens the backlog of misses
        // immediately, bypassing the multi-day conclusive backoff — even a stock still mid-backoff is
        // reconsidered when it was probed under an older version.
        var stock = new CommonStock
        {
            Website = "https://acme.com",
            InvestorRelationsCheckedAt = Utc("2026-06-14"),
            InvestorRelationsRetryAfter = Utc("2026-06-20"), // 6-day conclusive backoff, mid-flight
            WebsiteCheckedAt = Utc("2026-06-14"),
            InvestorRelationsDiscoveryVersion = version,
        };

        var eligible = InvestorRelationsDiscoveryService
            .PendingDiscovery(Now, CurrentVersion)
            .Compile();

        eligible(stock).Should().Be(expected);
    }

    [Theory]
    [InlineData("2026-06-14T20:00:00Z", false)] // 6h transient cooldown still running → hold
    [InlineData("2026-06-16T00:00:00Z", true)] // 34h gap is a conclusive backoff → bypass now
    public void PendingDiscovery_OlderVersionInTransientCooldown_WaitsOutTheCooldown(
        string retryAfter,
        bool expected
    )
    {
        // Contract: a transient (engine-unavailable) miss keeps the stock's OLD version stamp so a
        // version bump can still re-sweep it — but the bump must not bypass the short transient
        // cooldown itself, or a saturated sidecar would make every old-version stock immediately
        // re-eligible and livelock the sweep on the same top-priority names. Only a standing backoff
        // longer than the transient ceiling (a conclusive miss) is bypassed.
        var stock = new CommonStock
        {
            Website = "https://acme.com",
            InvestorRelationsCheckedAt = Utc("2026-06-14T14:00:00Z"),
            InvestorRelationsRetryAfter = Utc(retryAfter), // both still in the future at Now
            WebsiteCheckedAt = Utc("2026-06-01"),
            InvestorRelationsDiscoveryVersion = CurrentVersion - 1,
        };

        var eligible = InvestorRelationsDiscoveryService
            .PendingDiscovery(Utc("2026-06-14T15:00:00Z"), CurrentVersion)
            .Compile();

        eligible(stock).Should().Be(expected);
    }

    [Fact]
    public void PendingDiscovery_WebsiteFoundAfterLastProbe_IsEligibleWhileBackingOff()
    {
        // Contract: the reconciliation backstop for the website-discovered cascade — a stock probed
        // (and missed) BEFORE its website was found is re-probed even mid-backoff and at the current
        // version, because the input it needs only arrived afterwards.
        var stock = new CommonStock
        {
            Website = "https://acme.com",
            InvestorRelationsCheckedAt = Utc("2026-06-14"),
            InvestorRelationsRetryAfter = Utc("2026-06-20"), // still backing off
            WebsiteCheckedAt = Utc("2026-06-16"), // website found later
            InvestorRelationsDiscoveryVersion = CurrentVersion,
        };

        var eligible = InvestorRelationsDiscoveryService
            .PendingDiscovery(Now, CurrentVersion)
            .Compile();

        eligible(stock).Should().BeTrue();
    }

    [Theory]
    [InlineData(0, 1)] // first miss → the initial backoff
    [InlineData(1, 2)] // then double each subsequent miss…
    [InlineData(2, 4)]
    [InlineData(4, 8)]
    [InlineData(8, 15)] // 16 would exceed the cap → clamped to 15
    [InlineData(15, 15)] // and stays at the cap thereafter
    public void ComputeBackoff_DoublesFromInitialUpToCap(double previousDays, double expectedDays)
    {
        InvestorRelationsDiscoveryService
            .ComputeBackoff(TimeSpan.FromDays(previousDays), initialDays: 1, maxDays: 15)
            .Should()
            .Be(TimeSpan.FromDays(expectedDays));
    }
}
