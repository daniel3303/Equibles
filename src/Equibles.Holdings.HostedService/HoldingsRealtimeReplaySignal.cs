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
}
