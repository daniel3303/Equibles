using Equibles.Messaging.Attributes;
using Equibles.Messaging.Contracts.Activity;
using MassTransit;

namespace Equibles.Web.Services.Activity;

/// <summary>
/// Receives <see cref="ScraperActivity"/> events on the bus and hands them off
/// to the in-process broadcaster, which fans them out to connected SSE
/// clients. <c>AllowMultiple = true</c>: every web replica gets its own
/// endpoint so each replica forwards events to the browsers it's serving
/// (rather than competing for messages with the other replicas).
/// </summary>
[Consumer(allowMultiple: true)]
public class ScraperActivityConsumer : IConsumer<ScraperActivity>
{
    private readonly ActivityFeedBroadcaster _broadcaster;

    public ScraperActivityConsumer(ActivityFeedBroadcaster broadcaster)
    {
        _broadcaster = broadcaster;
    }

    public Task Consume(ConsumeContext<ScraperActivity> context)
    {
        _broadcaster.Publish(context.Message);
        return Task.CompletedTask;
    }
}
