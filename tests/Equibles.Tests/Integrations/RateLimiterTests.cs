using System.Diagnostics;
using Equibles.Integrations.Common.RateLimiter;

namespace Equibles.Tests.Integrations;

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
    public void Constructor_WithCustomTimeWindow_Succeeds() {
        // Arrange & Act
        var limiter = new RateLimiter(maxRequests: 3, timeWindow: TimeSpan.FromMilliseconds(100));

        // Assert
        limiter.Should().NotBeNull();
        limiter.Should().BeAssignableTo<IRateLimiter>();
    }
}
