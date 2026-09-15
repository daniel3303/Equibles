using Equibles.EquityMarkets.Data.Models;
using Equibles.EquityMarkets.HostedService;

namespace Equibles.UnitTests.EquityMarkets;

public class EquityMarketDirectoryWorkerTests
{
    private static readonly DateTime Now = new(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Refresh = TimeSpan.FromHours(24);
    private static readonly TimeSpan Retry = TimeSpan.FromMinutes(15);

    private static EquityMarketRegistration Row(
        DateTime? refreshedAt,
        DateTime? requestedAt = null
    ) =>
        new()
        {
            Code = "euronext-paris",
            DirectoryRefreshedAt = refreshedAt,
            DirectoryRefreshRequestedAt = requestedAt,
        };

    [Fact]
    public void AnOperatorRequest_AlwaysRuns_EvenRightAfterAFailedAttempt()
    {
        EquityMarketDirectoryWorker
            .IsDue(Row(Now.AddHours(-1), requestedAt: Now), Now.AddMinutes(-1), Now, Refresh, Retry)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void ANeverRefreshedMarket_RunsOnce_ThenWaitsTheRetryIntervalAfterAPassThatCapturedNothing()
    {
        EquityMarketDirectoryWorker.IsDue(Row(null), null, Now, Refresh, Retry).Should().BeTrue();
        EquityMarketDirectoryWorker
            .IsDue(Row(null), Now.AddMinutes(-5), Now, Refresh, Retry)
            .Should()
            .BeFalse("the source or FIRDS was not ready five minutes ago and the row is unchanged");
        EquityMarketDirectoryWorker
            .IsDue(Row(null), Now.AddMinutes(-16), Now, Refresh, Retry)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void ARequest_IsClearedOnlyByASuccessfulPassThatStartedAfterIt()
    {
        EquityMarketDirectoryWorker.RequestServed(Now, Now, true).Should().BeTrue();
        EquityMarketDirectoryWorker.RequestServed(null, null, true).Should().BeTrue();
        EquityMarketDirectoryWorker
            .RequestServed(Now, Now, false)
            .Should()
            .BeFalse("a failed pass leaves the request for the retry");
        EquityMarketDirectoryWorker
            .RequestServed(Now.AddMinutes(-10), Now, true)
            .Should()
            .BeFalse("an operator asked again while the pass ran");
        EquityMarketDirectoryWorker.RequestServed(null, Now, true).Should().BeFalse();
    }

    [Fact]
    public void ARefreshedMarket_RunsAgainOnlyWhenItsDirectoryIsOlderThanTheInterval()
    {
        EquityMarketDirectoryWorker
            .IsDue(Row(Now.AddHours(-23)), Now.AddHours(-23), Now, Refresh, Retry)
            .Should()
            .BeFalse();
        EquityMarketDirectoryWorker
            .IsDue(Row(Now.AddHours(-25)), Now.AddHours(-25), Now, Refresh, Retry)
            .Should()
            .BeTrue();
    }
}
