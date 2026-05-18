using Equibles.Core.AutoWiring;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Holdings.HostedService;

/// <summary>
/// In-process wake signal so <see cref="StockCusipChangedConsumer"/> can make
/// <see cref="HoldingsScraperWorker"/> re-run promptly after it invalidates the
/// ProcessedDataSet ledger — instead of the backfill waiting up to the worker's
/// 24h <c>SleepInterval</c> (and risking a same-cycle skip). Singleton so the
/// scoped consumer and the singleton hosted worker share one instance.
/// Requests coalesce: many CUSIP-change events in one FTD burst trigger a
/// single rescan (which re-imports every quarter for all tracked CUSIPs).
/// </summary>
[Service(ServiceLifetime.Singleton)]
public class HoldingsRescanSignal
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void RequestRescan()
    {
        if (_signal.CurrentCount == 0)
        {
            try
            {
                _signal.Release();
            }
            catch (SemaphoreFullException)
            {
                // A concurrent RequestRescan already raised it — one pending
                // rescan is enough.
            }
        }
    }

    public Task WaitAsync(CancellationToken cancellationToken) =>
        _signal.WaitAsync(cancellationToken);
}
