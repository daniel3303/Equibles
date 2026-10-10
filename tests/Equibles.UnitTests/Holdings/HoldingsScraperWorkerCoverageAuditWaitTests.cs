using Equibles.Holdings.HostedService;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.Holdings;

public class HoldingsScraperWorkerCoverageAuditWaitTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Day = TimeSpan.FromHours(24);

    [Fact]
    public void NoSkippedAudit_KeepsTheInterval() =>
        HoldingsScraperWorker.UntilCoverageAuditDue(Day, null, Now).Should().Be(Day);

    [Fact]
    public void AuditDueBeforeTheInterval_WakesWhenItFallsDue() =>
        HoldingsScraperWorker
            .UntilCoverageAuditDue(Day, Now.AddHours(3), Now)
            .Should()
            .Be(TimeSpan.FromHours(3));

    [Fact]
    public void AuditDueAfterTheInterval_KeepsTheInterval() =>
        HoldingsScraperWorker
            .UntilCoverageAuditDue(TimeSpan.FromMinutes(2), Now.AddHours(3), Now)
            .Should()
            .Be(TimeSpan.FromMinutes(2));

    [Fact]
    public void AuditAlreadyDue_WakesImmediately() =>
        HoldingsScraperWorker
            .UntilCoverageAuditDue(Day, Now.AddMinutes(-5), Now)
            .Should()
            .Be(TimeSpan.Zero);
}
