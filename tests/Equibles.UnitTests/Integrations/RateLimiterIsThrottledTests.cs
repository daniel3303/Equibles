using Equibles.Integrations.Common.RateLimiter;

namespace Equibles.UnitTests.Integrations;

/// <summary>
/// Pins RateLimiter.IsThrottled — the single source of truth callers use instead
/// of mirroring the pause in a separate flag. False before any pause, true while
/// a PauseFor window is in effect, false again once it elapses.
/// </summary>
public class RateLimiterIsThrottledTests
{
    [Fact]
    public void IsThrottled_NoPause_False()
    {
        var limiter = new RateLimiter(maxRequests: 5, timeWindow: TimeSpan.FromMilliseconds(100));

        limiter.IsThrottled.Should().BeFalse();
    }

    [Fact]
    public void IsThrottled_DuringPause_True()
    {
        var limiter = new RateLimiter(maxRequests: 5, timeWindow: TimeSpan.FromMilliseconds(100));

        limiter.PauseFor(TimeSpan.FromMinutes(10));

        limiter.IsThrottled.Should().BeTrue();
    }

    [Fact]
    public async Task IsThrottled_AfterPauseElapses_False()
    {
        var limiter = new RateLimiter(maxRequests: 5, timeWindow: TimeSpan.FromMilliseconds(100));

        limiter.PauseFor(TimeSpan.FromMilliseconds(20));
        await Task.Delay(60);

        limiter.IsThrottled.Should().BeFalse();
    }
}
