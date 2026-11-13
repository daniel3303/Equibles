using Equibles.Integrations.Common.RateLimiter;

namespace Equibles.UnitTests.Integrations;

public class RateLimiterReservedShareTests
{
    [Theory]
    // Nobody else is waiting: either class may use the whole window.
    [InlineData(true, 39, 0, false, true)]
    [InlineData(false, 0, 39, false, true)]
    // Background callers are waiting: urgent stops at the window minus the reserve.
    [InlineData(true, 27, 0, true, true)]
    [InlineData(true, 28, 0, true, false)]
    // Urgent callers are waiting: background keeps only its reserve.
    [InlineData(false, 20, 11, true, true)]
    [InlineData(false, 20, 12, true, false)]
    public void ASlotGoesToTheClassStillInsideItsShare(
        bool urgent,
        int urgentInWindow,
        int backgroundInWindow,
        bool otherClassWaiting,
        bool expected
    )
    {
        RateLimiter
            .MayTake(urgent, urgentInWindow, backgroundInWindow, otherClassWaiting, 40, 12)
            .Should()
            .Be(expected);
    }

    [Fact]
    public async Task BackgroundCallersKeepTheirReserveWhileUrgentOnesQueue()
    {
        var limiter = new RateLimiter(
            maxRequests: 3,
            timeWindow: TimeSpan.FromMilliseconds(600),
            backgroundReserve: 1
        );
        for (var index = 0; index < 3; index++)
            await limiter.WaitAsync(urgent: true);

        // The window is full. One background caller queues first, then three urgent ones: when
        // the window reopens, urgent may take two slots and the third belongs to the reserve.
        var background = limiter.WaitAsync(urgent: false);
        await Task.Delay(100);
        var urgent = Enumerable.Range(0, 3).Select(_ => limiter.WaitAsync(urgent: true)).ToArray();

        await background;

        urgent.Count(task => task.IsCompleted).Should().BeLessThan(3);
        await Task.WhenAll(urgent);
    }

    [Fact]
    public void AReserveThatLeavesUrgentCallersNothingIsRejected()
    {
        var act = () => new RateLimiter(maxRequests: 5, backgroundReserve: 5);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
