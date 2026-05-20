using System.Threading.Channels;
using Equibles.Messaging.Contracts.Activity;

namespace Equibles.Web.Services.Activity;

public sealed class ActivityFeedSubscription : IDisposable
{
    private readonly ActivityFeedBroadcaster _broadcaster;
    private bool _disposed;

    internal ActivityFeedSubscription(
        Guid id,
        IReadOnlyList<ScraperActivity> backlog,
        ChannelReader<ScraperActivity> reader,
        ActivityFeedBroadcaster broadcaster
    )
    {
        Id = id;
        Backlog = backlog;
        Reader = reader;
        _broadcaster = broadcaster;
    }

    public Guid Id { get; }
    public IReadOnlyList<ScraperActivity> Backlog { get; }
    public ChannelReader<ScraperActivity> Reader { get; }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _broadcaster.Unsubscribe(Id);
    }
}
