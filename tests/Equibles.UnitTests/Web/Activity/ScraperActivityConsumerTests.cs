using Equibles.Messaging.Contracts.Activity;
using Equibles.Web.Services.Activity;
using MassTransit;
using NSubstitute;

namespace Equibles.UnitTests.Web.Activity;

public class ScraperActivityConsumerTests
{
    private readonly ActivityFeedBroadcaster _broadcaster = new();
    private readonly ScraperActivityConsumer _sut;

    public ScraperActivityConsumerTests() => _sut = new ScraperActivityConsumer(_broadcaster);

    [Fact]
    public async Task Consume_HandsMessageOffToBroadcaster()
    {
        var activity = new ScraperActivity(
            Source: "SEC",
            Severity: ScraperActivitySeverity.Info,
            Message: "fetching 10-K",
            Timestamp: DateTimeOffset.UtcNow,
            CorrelationId: Guid.NewGuid().ToString()
        );

        using var subscription = _broadcaster.Subscribe(backlogSize: 0);

        var context = Substitute.For<ConsumeContext<ScraperActivity>>();
        context.Message.Returns(activity);

        await _sut.Consume(context);

        subscription.Reader.TryRead(out var observed).Should().BeTrue();
        observed.Should().Be(activity);
    }
}
