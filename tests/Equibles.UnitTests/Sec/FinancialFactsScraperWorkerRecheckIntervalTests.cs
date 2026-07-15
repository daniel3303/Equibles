using Equibles.Sec.FinancialFacts.HostedService.Configuration;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsScraperWorkerRecheckIntervalTests
{
    // The recheck window must sit BELOW the 24h default sleep: a completed
    // cycle plus a full sleep has to find every company stale again, or the
    // steady-state cadence silently stretches beyond a day. And it must be
    // large relative to a sweep's duration (~1.5h for the full universe), or
    // a restart re-downloads companies the aborted sweep just checked. A
    // "harmonise the defaults" cleanup that set it to 24 would break the
    // former invariant with no test failing anywhere else.
    [Fact]
    public void DefaultRecheckIntervalIsBelowDefaultSleepInterval()
    {
        var options = new FinancialFactsScraperOptions();

        options.RecheckIntervalHours.Should().Be(20);
        options.RecheckIntervalHours.Should().BeLessThan(options.SleepIntervalHours);
    }
}
