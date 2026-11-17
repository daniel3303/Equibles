using Equibles.Core.AutoWiring;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Holdings.HostedService;

// This wake-up is only an optimization; the replay watermark is durable across restarts.
[Service(ServiceLifetime.Singleton)]
public sealed class HoldingsRealtimeReplaySignal
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void RequestReplay()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException) { }
    }

    public Task WaitAsync(CancellationToken cancellationToken) =>
        _signal.WaitAsync(cancellationToken);

    // A newly queued filing needs only the recovery pass, never a re-read of the daily indexes.
    private readonly SemaphoreSlim _recovery = new(0, 1);

    public void RequestRecovery()
    {
        try
        {
            _recovery.Release();
        }
        catch (SemaphoreFullException) { }
    }

    public Task WaitForRecoveryAsync(CancellationToken cancellationToken) =>
        _recovery.WaitAsync(cancellationToken);
}
