using Equibles.Integrations.Common.RateLimiter;

namespace Equibles.UnitTests.Integrations;

public class RateLimiterZeroCapacityTests
{
    [Fact]
    public void Constructor_ZeroMaxRequests_RejectsInvalidCapacity()
    {
        // A limiter whose capacity is zero can never release a request — it is an
        // impossible configuration. The .NET convention for an out-of-range count
        // argument (cf. SemaphoreSlim's initialCount) is to fail fast at construction
        // with ArgumentOutOfRangeException. Instead the value is silently accepted and
        // the FIRST WaitAsync throws InvalidOperationException ("queue empty") from
        // Queue.Peek deep inside CalculateWaitTime (0 < 0 is false, so the empty queue
        // is peeked) — an internal invariant leaking out as the wrong exception type.
        var act = () => new RateLimiter(maxRequests: 0, timeWindow: TimeSpan.FromMinutes(1));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
