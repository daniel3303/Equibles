using Equibles.Integrations.Common.Retry;

namespace Equibles.UnitTests.Integrations;

public class RetryBackoffExponentialTests
{
    // Contract (doc): exponential backoff f(n) = 2^(n+1) seconds — f(0)=2s, f(1)=4s. The +1
    // shift is load-bearing: dropping it halves every interval (f(0)=1s) and turns an upstream
    // throttle into a hard ban. Two points lock the whole family: f(0)=2 alone passes for the
    // plausible 2^n+1 typo (also 2), so f(1)=4 is needed to rule it out (2^1+1=3≠4). Existing
    // coverage only counts Yahoo retries; the arithmetic itself was never pinned.
    [Fact]
    public void Exponential_FirstTwoAttempts_FollowDocumentedTwoToThePowerNPlusOne()
    {
        RetryBackoff.Exponential(0).Should().Be(TimeSpan.FromSeconds(2));
        RetryBackoff.Exponential(1).Should().Be(TimeSpan.FromSeconds(4));
    }
}
