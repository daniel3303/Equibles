using Equibles.Integrations.Common.RateLimiter;

namespace Equibles.UnitTests.EquityMarkets;

// Counts what a client asked its pace for without spending the wait.
internal sealed class CountingRateLimiter : IRateLimiter
{
    public int Waits { get; private set; }

    public Task WaitAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Waits++;
        return Task.CompletedTask;
    }

    public void PauseFor(TimeSpan duration) { }

    public bool IsThrottled => false;
}
