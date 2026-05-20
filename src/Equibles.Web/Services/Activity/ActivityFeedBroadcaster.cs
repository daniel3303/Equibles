using System.Collections.Concurrent;
using System.Threading.Channels;
using Equibles.Core.AutoWiring;
using Equibles.Messaging.Contracts.Activity;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Web.Services.Activity;

/// <summary>
/// In-process fan-out hub for live scraper activity events. The MassTransit
/// consumer pushes each incoming <see cref="ScraperActivity"/> here; every
/// connected SSE client gets its own bounded channel so a slow browser can't
/// back-pressure the bus (oldest event is dropped instead).
/// A small ring buffer is kept so a freshly-connected client gets a usable
/// tail — the activity feed is otherwise "what's happening right now".
/// </summary>
[Service(ServiceLifetime.Singleton)]
public class ActivityFeedBroadcaster
{
    public const int BufferCapacity = 500;
    public const int SubscriberQueueCapacity = 256;

    private readonly ConcurrentDictionary<Guid, Channel<ScraperActivity>> _subscribers = new();
    private readonly LinkedList<ScraperActivity> _buffer = new();
    private readonly object _bufferLock = new();

    public void Publish(ScraperActivity activity)
    {
        lock (_bufferLock)
        {
            _buffer.AddLast(activity);
            while (_buffer.Count > BufferCapacity)
            {
                _buffer.RemoveFirst();
            }
        }

        foreach (var subscriber in _subscribers.Values)
        {
            // Bounded channel with DropOldest — a paused tab never blocks publish.
            subscriber.Writer.TryWrite(activity);
        }
    }

    public ActivityFeedSubscription Subscribe(int backlogSize = 200)
    {
        if (backlogSize < 0)
            backlogSize = 0;
        if (backlogSize > BufferCapacity)
            backlogSize = BufferCapacity;

        var channel = Channel.CreateBounded<ScraperActivity>(
            new BoundedChannelOptions(SubscriberQueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            }
        );

        var id = Guid.NewGuid();
        _subscribers[id] = channel;

        List<ScraperActivity> backlog;
        lock (_bufferLock)
        {
            var skip = Math.Max(0, _buffer.Count - backlogSize);
            backlog = _buffer.Skip(skip).ToList();
        }

        return new ActivityFeedSubscription(id, backlog, channel.Reader, this);
    }

    internal void Unsubscribe(Guid id)
    {
        if (_subscribers.TryRemove(id, out var channel))
        {
            channel.Writer.TryComplete();
        }
    }

    public int SubscriberCount => _subscribers.Count;
}
