using Equibles.Messaging.Contracts.Activity;
using Equibles.Web.Services.Activity;

namespace Equibles.UnitTests.Web.Activity;

public class ActivityFeedBroadcasterTests
{
    private readonly ActivityFeedBroadcaster _sut = new();

    [Fact]
    public void Publish_FansOutToEverySubscriber()
    {
        using var a = _sut.Subscribe();
        using var b = _sut.Subscribe();
        var activity = MakeActivity("SEC", "fetching 10-K");

        _sut.Publish(activity);

        a.Reader.TryRead(out var fromA).Should().BeTrue();
        b.Reader.TryRead(out var fromB).Should().BeTrue();
        fromA.Should().Be(activity);
        fromB.Should().Be(activity);
    }

    [Fact]
    public void Subscribe_AfterPublish_ReplaysBufferedEvents()
    {
        var first = MakeActivity("SEC", "first");
        var second = MakeActivity("Yahoo", "second");
        _sut.Publish(first);
        _sut.Publish(second);

        using var subscription = _sut.Subscribe();

        subscription
            .Backlog.Should()
            .Equal(new[] { first, second }, "newly-connected clients need recent context");
    }

    [Fact]
    public void Subscribe_RequestingFewerThanBuffered_TrimsToTheNewestEvents()
    {
        var first = MakeActivity("SEC", "first");
        var second = MakeActivity("SEC", "second");
        var third = MakeActivity("SEC", "third");
        _sut.Publish(first);
        _sut.Publish(second);
        _sut.Publish(third);

        using var subscription = _sut.Subscribe(backlogSize: 2);

        subscription.Backlog.Should().Equal(second, third);
    }

    [Fact]
    public void Subscribe_NegativeBacklog_ReturnsEmpty()
    {
        _sut.Publish(MakeActivity("SEC", "noise"));

        using var subscription = _sut.Subscribe(backlogSize: -5);

        subscription.Backlog.Should().BeEmpty();
    }

    [Fact]
    public void Subscribe_BacklogLargerThanBuffer_ClampsToBufferCapacity()
    {
        for (var i = 0; i < 50; i++)
        {
            _sut.Publish(MakeActivity("SEC", $"msg-{i}"));
        }

        using var subscription = _sut.Subscribe(
            backlogSize: ActivityFeedBroadcaster.BufferCapacity + 1000
        );

        // Only 50 events were ever published, so the clamp lands well below
        // BufferCapacity — the assert is that nothing extra is invented.
        subscription.Backlog.Should().HaveCount(50);
    }

    [Fact]
    public void Dispose_CalledTwice_IsIdempotent()
    {
        var subscription = _sut.Subscribe();
        subscription.Dispose();
        var act = () => subscription.Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_RemovesSubscriberSoFuturePublishesIgnoreIt()
    {
        var subscription = _sut.Subscribe();
        _sut.SubscriberCount.Should().Be(1);

        subscription.Dispose();

        _sut.SubscriberCount.Should().Be(0);
    }

    [Fact]
    public void Publish_FloodingBuffer_KeepsLatestBufferCapacityItems()
    {
        for (var i = 0; i < ActivityFeedBroadcaster.BufferCapacity + 50; i++)
        {
            _sut.Publish(MakeActivity("SEC", $"msg-{i}"));
        }

        using var subscription = _sut.Subscribe(
            backlogSize: ActivityFeedBroadcaster.BufferCapacity + 1
        );

        subscription
            .Backlog.Should()
            .HaveCount(ActivityFeedBroadcaster.BufferCapacity, "buffer is a ring");
        subscription
            .Backlog.Last()
            .Message.Should()
            .Be($"msg-{ActivityFeedBroadcaster.BufferCapacity + 49}");
    }

    [Fact]
    public void Publish_FloodingSlowSubscriber_DropsOldestUnread()
    {
        // Subscribe but never read — pretend a paused tab.
        using var subscription = _sut.Subscribe();

        var flood = ActivityFeedBroadcaster.SubscriberQueueCapacity + 10;
        for (var i = 0; i < flood; i++)
        {
            _sut.Publish(MakeActivity("SEC", $"msg-{i}"));
        }

        // The channel only retains up to its bounded capacity; older items are
        // dropped on overflow (FullMode = DropOldest), so the bus and the
        // other subscribers never block waiting on this slow client.
        var drained = new List<ScraperActivity>();
        while (subscription.Reader.TryRead(out var item))
        {
            drained.Add(item);
        }

        drained
            .Should()
            .HaveCountLessThanOrEqualTo(ActivityFeedBroadcaster.SubscriberQueueCapacity);
        drained
            .Last()
            .Message.Should()
            .Be(
                $"msg-{flood - 1}",
                "the newest event must survive even when the subscriber is slow"
            );
    }

    private static ScraperActivity MakeActivity(string source, string message) =>
        new(
            source,
            ScraperActivitySeverity.Info,
            message,
            DateTimeOffset.UtcNow,
            CorrelationId: Guid.NewGuid().ToString()
        );
}
