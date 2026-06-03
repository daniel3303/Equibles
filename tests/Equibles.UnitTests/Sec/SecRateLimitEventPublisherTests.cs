using Equibles.Messaging.Contracts.Activity;
using Equibles.Sec.HostedService.Services;
using MassTransit;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins SecRateLimitEventPublisher's edge detection. SecEdgarClient calls
/// RateLimited on every throttled response and Reachable on every success; the
/// publisher must emit exactly one SecRateLimitBlocked on the
/// reachable→blocked edge and one SecRateLimitCleared on the blocked→reachable
/// edge, so a retry storm or parallel fetches don't spam the feed.
/// </summary>
public class SecRateLimitEventPublisherTests
{
    private const string Url = "https://www.sec.gov/Archives/edgar/data/1/0-0.txt";

    [Fact]
    public async Task RateLimited_RepeatedWhileBlocked_PublishesBlockedOnce()
    {
        var bus = Substitute.For<IBus>();
        var sut = new SecRateLimitEventPublisher(bus);

        await sut.RateLimited(TimeSpan.FromMinutes(10), Url);
        await sut.RateLimited(TimeSpan.FromMinutes(10), Url);
        await sut.RateLimited(TimeSpan.FromMinutes(10), Url);

        await bus.Received(1).Publish(Arg.Any<SecRateLimitBlocked>());
    }

    [Fact]
    public async Task Reachable_AfterBlocked_PublishesClearedOnce()
    {
        var bus = Substitute.For<IBus>();
        var sut = new SecRateLimitEventPublisher(bus);
        await sut.RateLimited(TimeSpan.FromMinutes(10), Url);

        await sut.Reachable(Url);
        await sut.Reachable(Url);

        await bus.Received(1).Publish(Arg.Any<SecRateLimitCleared>());
    }

    [Fact]
    public async Task Reachable_WithoutPriorBlock_PublishesNothing()
    {
        var bus = Substitute.For<IBus>();
        var sut = new SecRateLimitEventPublisher(bus);

        await sut.Reachable(Url);

        await bus.DidNotReceive().Publish(Arg.Any<SecRateLimitCleared>());
    }

    [Fact]
    public async Task BlockedThenClearedThenBlocked_PublishesBothEdgesAgain()
    {
        var bus = Substitute.For<IBus>();
        var sut = new SecRateLimitEventPublisher(bus);

        await sut.RateLimited(TimeSpan.FromMinutes(10), Url);
        await sut.Reachable(Url);
        await sut.RateLimited(TimeSpan.FromMinutes(10), Url);

        await bus.Received(2).Publish(Arg.Any<SecRateLimitBlocked>());
        await bus.Received(1).Publish(Arg.Any<SecRateLimitCleared>());
    }
}
