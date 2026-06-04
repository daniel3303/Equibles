using Equibles.Messaging.Contracts.Activity;
using Equibles.Sec.HostedService.Services;
using MassTransit;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Adversarial sibling to <see cref="SecRateLimitEventPublisherTests"/>, which
/// only counts published events with <c>Arg.Any</c>. The block event drives the
/// backoffice live-activity feed, so a refactor that swaps or drops
/// <c>RateLimited</c>'s (pause, url) arguments would still emit exactly one event
/// and leave every count-only test green. This pins that the payload actually
/// carries the pause and url it was called with.
/// </summary>
public class SecRateLimitEventPublisherBlockedPayloadTests
{
    private const string Url = "https://www.sec.gov/Archives/edgar/data/1/0-0.txt";

    [Fact]
    public async Task RateLimited_OnBlockEdge_PublishesEventCarryingPauseAndUrl()
    {
        var bus = Substitute.For<IBus>();
        var sut = new SecRateLimitEventPublisher(bus);
        var pause = TimeSpan.FromMinutes(7);

        await sut.RateLimited(pause, Url);

        await bus.Received(1)
            .Publish(Arg.Is<SecRateLimitBlocked>(e => e.Pause == pause && e.Url == Url));
    }
}
