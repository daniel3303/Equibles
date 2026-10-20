using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings.FundOwnership;

public class FundOwnershipDrainWorkerTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void IsDue_ANeverPublishedQuarterIsDue()
    {
        FundOwnershipDrainWorker.IsDue(null, "fp", Now).Should().BeTrue();
        FundOwnershipDrainWorker
            .IsDue(new FundOwnershipQuarterState { InputFingerprint = "fp" }, "fp", Now)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void IsDue_ACurrentQuarterWaitsUntilItsInputsOrVersionChange()
    {
        var state = Published(Now.AddDays(-1));

        FundOwnershipDrainWorker.IsDue(state, "fp", Now).Should().BeFalse();
        FundOwnershipDrainWorker.IsDue(state, "fp-2", Now).Should().BeTrue();
        state.CalculationVersion = FundOwnershipRefreshService.CalculationVersion - 1;
        FundOwnershipDrainWorker.IsDue(state, "fp", Now).Should().BeTrue();
    }

    [Fact]
    public void IsDue_AStaleQuarterIsRebuiltWithUnchangedInputs()
    {
        FundOwnershipDrainWorker
            .IsDue(
                Published(Now - FundOwnershipRefreshService.RefreshAfter - TimeSpan.FromHours(1)),
                "fp",
                Now
            )
            .Should()
            .BeTrue();
    }

    [Fact]
    public void IsDue_AFailedQuarterWaitsOutTheRetryDelay()
    {
        var state = Published(Now.AddDays(-2));
        state.FailureCount = 1;
        state.AttemptedAt = Now.AddMinutes(-10);

        FundOwnershipDrainWorker.IsDue(state, "fp-2", Now).Should().BeFalse();
        state.AttemptedAt = Now - FundOwnershipDrainWorker.RetryAfter - TimeSpan.FromMinutes(1);
        FundOwnershipDrainWorker.IsDue(state, "fp-2", Now).Should().BeTrue();
    }

    private static FundOwnershipQuarterState Published(DateTime computedAt) =>
        new()
        {
            ReportDate = new DateOnly(2026, 6, 30),
            InputFingerprint = "fp",
            ComputedAt = computedAt,
            CalculationVersion = FundOwnershipRefreshService.CalculationVersion,
        };
}
