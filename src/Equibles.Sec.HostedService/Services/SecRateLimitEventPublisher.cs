using Equibles.Core.AutoWiring;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Messaging.Contracts.Activity;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Publishes <see cref="SecRateLimitBlocked"/> / <see cref="SecRateLimitCleared"/>
/// on the bus when SEC's rate-limit state changes, so the backoffice
/// live-activity feed (and any other subscriber) can show when scraping is
/// blocked and when it recovers.
///
/// Registered as a singleton so the edge detection is process-global: a 429 seen
/// by any of the worker's concurrent SEC processors publishes exactly one
/// "blocked" event, and the next successful request publishes exactly one
/// "cleared" event — no duplicates from the retry loop or parallel fetches.
/// </summary>
[Service(ServiceLifetime.Singleton, typeof(ISecRateLimitNotifier))]
public class SecRateLimitEventPublisher : ISecRateLimitNotifier
{
    private readonly IBus _bus;

    // 0 = reachable, 1 = blocked. Flipped with Interlocked so only the thread
    // that observes the edge publishes.
    private int _blocked;

    public SecRateLimitEventPublisher(IBus bus)
    {
        _bus = bus;
    }

    public Task RateLimited(TimeSpan pause, string url)
    {
        if (Interlocked.Exchange(ref _blocked, 1) == 0)
        {
            return _bus.Publish(new SecRateLimitBlocked(DateTimeOffset.UtcNow, pause, url));
        }

        return Task.CompletedTask;
    }

    public Task Reachable(string url)
    {
        if (Interlocked.Exchange(ref _blocked, 0) == 1)
        {
            return _bus.Publish(new SecRateLimitCleared(DateTimeOffset.UtcNow, url));
        }

        return Task.CompletedTask;
    }
}
