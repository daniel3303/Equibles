using Equibles.Holdings.HostedService;

namespace Equibles.UnitTests.Holdings;

public class HoldingsRescanSignalTests
{
    // GH-852: a requested rescan releases a waiter promptly.
    [Fact]
    public async Task RequestRescan_ReleasesWaiter()
    {
        var signal = new HoldingsRescanSignal();

        signal.RequestRescan();

        var wait = signal.WaitAsync(CancellationToken.None);
        (await Task.WhenAny(wait, Task.Delay(TimeSpan.FromSeconds(1)))).Should().Be(wait);
    }

    // Requests coalesce: many CUSIP-change events in one FTD burst must trigger
    // a single rescan, not one per event (one rescan re-imports every quarter
    // for all tracked CUSIPs anyway).
    [Fact]
    public async Task RequestRescan_Coalesces_OnlyOnePendingRescan()
    {
        var signal = new HoldingsRescanSignal();

        signal.RequestRescan();
        signal.RequestRescan();
        signal.RequestRescan();

        await signal.WaitAsync(CancellationToken.None); // consume the single permit

        var second = signal.WaitAsync(CancellationToken.None);
        (await Task.WhenAny(second, Task.Delay(TimeSpan.FromMilliseconds(300))))
            .Should()
            .NotBe(second, "coalesced requests must leave only one pending rescan");
    }
}
