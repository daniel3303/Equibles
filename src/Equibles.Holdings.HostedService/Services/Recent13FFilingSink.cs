using Equibles.Core.AutoWiring;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Queues each 13F the latest-filings feed lists and wakes the 13F worker, so a filing imports
/// minutes after SEC accepts it instead of after the day's index is published that night.
/// </summary>
[Service(ServiceLifetime.Scoped, typeof(IRecentFilingSink))]
public class Recent13FFilingSink(
    Realtime13FIngestionService ingestion,
    HoldingsRealtimeReplaySignal signal
) : IRecentFilingSink
{
    public async Task Offer(
        IReadOnlyList<EdgarRecentFilingEntry> entries,
        CancellationToken cancellationToken
    )
    {
        if (await ingestion.QueueFeedFilings(entries, cancellationToken) > 0)
            signal.RequestRecovery();
    }
}
