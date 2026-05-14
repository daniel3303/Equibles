using System.Diagnostics;
using Equibles.Integrations.Common.RateLimiter;

namespace Equibles.UnitTests.Integrations;

public class RateLimiterTests
{
    [Fact]
    public async Task RequestsWithinLimit_CompleteImmediately()
    {
        // Arrange: allow 5 requests per 100ms window
        var limiter = new RateLimiter(maxRequests: 5, timeWindow: TimeSpan.FromMilliseconds(100));
        var sw = Stopwatch.StartNew();

        // Act: make 3 requests (well within the limit of 5)
        for (var i = 0; i < 3; i++)
        {
            await limiter.WaitAsync();
        }

        sw.Stop();

        // Assert: should complete nearly instantly (under 50ms)
        sw.ElapsedMilliseconds.Should().BeLessThan(50);
    }

    [Fact]
    public void DefaultConstructor_SetsExpectedDefaults()
    {
        // Arrange & Act
        var limiter = new RateLimiter();

        // Assert: default is 5 requests per 1 minute — just verify construction succeeds
        limiter.Should().NotBeNull();
        limiter.Should().BeAssignableTo<IRateLimiter>();
    }

    [Fact]
    public void CustomConstructorValues_AreAccepted()
    {
        // Arrange & Act
        var limiter = new RateLimiter(maxRequests: 10, timeWindow: TimeSpan.FromSeconds(30));

        // Assert: construction with custom values succeeds
        limiter.Should().NotBeNull();
        limiter.Should().BeAssignableTo<IRateLimiter>();
    }

    [Fact]
    public async Task PauseFor_DelaysSubsequentWaitAsyncCalls()
    {
        // Arrange: generous limit so throttling comes only from PauseFor
        var limiter = new RateLimiter(maxRequests: 100, timeWindow: TimeSpan.FromSeconds(10));

        // Act: pause for 200ms, then measure how long WaitAsync takes
        limiter.PauseFor(TimeSpan.FromMilliseconds(200));

        var sw = Stopwatch.StartNew();
        await limiter.WaitAsync();
        sw.Stop();

        // Assert: should have waited at least ~150ms (allowing tolerance)
        sw.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(150);
    }

    [Fact]
    public async Task MultipleRapidRequests_WithinLimit_AllSucceedQuickly()
    {
        // Arrange: allow 5 requests in a 500ms window
        var limiter = new RateLimiter(maxRequests: 5, timeWindow: TimeSpan.FromMilliseconds(500));
        var sw = Stopwatch.StartNew();

        // Act: fire 5 requests concurrently (exactly at the limit)
        var tasks = Enumerable.Range(0, 5).Select(_ => limiter.WaitAsync()).ToArray();
        await Task.WhenAll(tasks);

        sw.Stop();

        // Assert: all 5 should complete quickly since they're within the limit
        sw.ElapsedMilliseconds.Should().BeLessThan(50);
    }

    [Fact]
    public async Task WaitAsync_FillsCapacity_NextRequestWaitsForOldestToAgeOut()
    {
        // RateLimiter is the only thing standing between our scrapers and rate-limit bans
        // from FRED, FINRA, Yahoo, CBOE, and CFTC. Its core function is throttling: when
        // the in-window request count reaches `maxRequests`, the next WaitAsync MUST block
        // until the oldest request ages out of the window. The existing tests cover the
        // "within capacity" path (everything fast) and PauseFor (external 429 reaction)
        // — neither exercises the capacity-throttle branch in CalculateWaitTime where
        // `_requestTimes.Count >= _maxRequests` returns the positive remainder time.
        //
        // The risk this test pins: a refactor that simplifies CalculateWaitTime to always
        // return TimeSpan.Zero (or that inverts the `Count < _maxRequests` predicate, or
        // that drops the recursive `await WaitAsync()` at the end of WaitAsync) would
        // let through unlimited requests at the speed of the await machinery. None of the
        // existing tests would fail — they all use maxRequests sized comfortably above
        // their request count. The first sign of the regression would be production bans
        // from the rate-limited APIs, with no visible test failure in CI.
        //
        // Construction: capacity=2 in a 300ms window. Fire 2 requests immediately to fill
        // the queue, then time the 3rd. The 3rd must wait long enough for the first
        // request to age out — at least ~200ms (300ms window minus the small gap between
        // requests one and three).
        var limiter = new RateLimiter(maxRequests: 2, timeWindow: TimeSpan.FromMilliseconds(300));
        await limiter.WaitAsync();
        await limiter.WaitAsync();

        var sw = Stopwatch.StartNew();
        await limiter.WaitAsync();
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(200);
    }

    [Fact]
    public async Task PauseFor_ShorterDurationFollowingLongerPause_DoesNotShortenExistingPause()
    {
        // PauseFor's body holds a guard: `if (newPauseUntil > _pauseUntil) _pauseUntil = ...;`
        // The guard is load-bearing for cascading rate-limit backoffs. Concrete production
        // scenario: SEC EDGAR returns a 429 with Retry-After=300s, SendWithRetryAsync calls
        // PauseFor(5min). Some time later another worker on the same shared RateLimiter gets
        // a less-aggressive 429 (Retry-After=1s) — without the guard, the second
        // PauseFor(1s) would OVERWRITE the still-active 5-minute pause, releasing every
        // queued request after only one second of the SEC-mandated 5-minute cooldown.
        // The next request burst would hit SEC's load balancer before the cooldown
        // expires, escalate to a longer ban, and the scraper would spiral into ever-
        // increasing 429 windows.
        //
        // The existing PauseFor_DelaysSubsequentWaitAsyncCalls test only exercises a single
        // PauseFor in isolation, so it can't catch a refactor that drops the if-guard. A
        // "simplification" PR that wrote `_pauseUntil = newPauseUntil;` unconditionally
        // (or `_pauseUntil = Max(...)` with a wrong comparison) would pass every existing
        // pin and silently degrade rate-limit protection in production.
        //
        // Construction: a generous in-window capacity so request-rate throttling can't
        // confound the pause assertion. Issue a long PauseFor (500ms), immediately follow
        // with a shorter PauseFor (50ms), then time WaitAsync. The assertion is that
        // WaitAsync blocks for at least most of the long pause — proves the longer pause
        // survived. A regression that lets the shorter pause win would complete in ~50ms,
        // far below the 350ms threshold.
        var limiter = new RateLimiter(maxRequests: 100, timeWindow: TimeSpan.FromSeconds(10));

        limiter.PauseFor(TimeSpan.FromMilliseconds(500));
        limiter.PauseFor(TimeSpan.FromMilliseconds(50));

        var sw = Stopwatch.StartNew();
        await limiter.WaitAsync();
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(350);
    }

    [Fact]
    public void Constructor_WithCustomTimeWindow_Succeeds()
    {
        // Arrange & Act
        var limiter = new RateLimiter(maxRequests: 3, timeWindow: TimeSpan.FromMilliseconds(100));

        // Assert
        limiter.Should().NotBeNull();
        limiter.Should().BeAssignableTo<IRateLimiter>();
    }
}
