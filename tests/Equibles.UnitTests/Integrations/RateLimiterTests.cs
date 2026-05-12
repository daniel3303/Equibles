using System.Diagnostics;
using Equibles.Integrations.Common.RateLimiter;

namespace Equibles.UnitTests.Integrations;

public class RateLimiterTests {
    [Fact]
    public async Task RequestsWithinLimit_CompleteImmediately() {
        // Arrange: allow 5 requests per 100ms window
        var limiter = new RateLimiter(maxRequests: 5, timeWindow: TimeSpan.FromMilliseconds(100));
        var sw = Stopwatch.StartNew();

        // Act: make 3 requests (well within the limit of 5)
        for (var i = 0; i < 3; i++) {
            await limiter.WaitAsync();
        }

        sw.Stop();

        // Assert: should complete nearly instantly (under 50ms)
        sw.ElapsedMilliseconds.Should().BeLessThan(50);
    }

    [Fact]
    public void DefaultConstructor_SetsExpectedDefaults() {
        // Arrange & Act
        var limiter = new RateLimiter();

        // Assert: default is 5 requests per 1 minute — just verify construction succeeds
        limiter.Should().NotBeNull();
        limiter.Should().BeAssignableTo<IRateLimiter>();
    }

    [Fact]
    public void CustomConstructorValues_AreAccepted() {
        // Arrange & Act
        var limiter = new RateLimiter(maxRequests: 10, timeWindow: TimeSpan.FromSeconds(30));

        // Assert: construction with custom values succeeds
        limiter.Should().NotBeNull();
        limiter.Should().BeAssignableTo<IRateLimiter>();
    }

    [Fact]
    public async Task PauseFor_DelaysSubsequentWaitAsyncCalls() {
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
    public async Task MultipleRapidRequests_WithinLimit_AllSucceedQuickly() {
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
    public async Task WaitAsync_FillsCapacity_NextRequestWaitsForOldestToAgeOut() {
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
    public void Constructor_WithCustomTimeWindow_Succeeds() {
        // Arrange & Act
        var limiter = new RateLimiter(maxRequests: 3, timeWindow: TimeSpan.FromMilliseconds(100));

        // Assert
        limiter.Should().NotBeNull();
        limiter.Should().BeAssignableTo<IRateLimiter>();
    }
}
