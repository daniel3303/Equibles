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
            .IsDue(
                Row(Now.AddHours(-1), requestedAt: Now),
                Now.AddMinutes(-1),
                9,
                Now,
                Refresh,
                Retry
            )
            .Should()
            .BeTrue();
    }

    [Fact]
    public void ANeverRefreshedMarket_RunsOnce_ThenWaitsTheRetryIntervalAfterAPassThatCapturedNothing()
    {
        EquityMarketDirectoryWorker
            .IsDue(Row(null), null, 0, Now, Refresh, Retry)
            .Should()
            .BeTrue();
        EquityMarketDirectoryWorker
            .IsDue(Row(null), Now.AddMinutes(-5), 1, Now, Refresh, Retry)
            .Should()
            .BeFalse("the source or FIRDS was not ready five minutes ago and the row is unchanged");
        EquityMarketDirectoryWorker
            .IsDue(Row(null), Now.AddMinutes(-16), 1, Now, Refresh, Retry)
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
    public void AStandingRequest_RunsOncePerPress_NotEveryControlTick()
    {
        var requested = Now.AddMinutes(-30);
        EquityMarketDirectoryWorker
            .IsDue(Row(null, requestedAt: requested), null, 0, Now, Refresh, Retry)
            .Should()
            .BeTrue("nothing has tried it since the operator asked");
        EquityMarketDirectoryWorker
            .IsDue(Row(null, requestedAt: requested), Now.AddMinutes(-1), 3, Now, Refresh, Retry)
            .Should()
            .BeFalse(
                "the request was served a minute ago and the pass failed; it is not a licence to loop"
            );
        EquityMarketDirectoryWorker
            .IsDue(Row(null, requestedAt: Now), Now.AddMinutes(-1), 3, Now, Refresh, Retry)
            .Should()
            .BeTrue("the operator asked again after that attempt");
    }

    [Fact]
    public void ARepeatedlyFailingMarket_WaitsLongerEachTimeUpToTheRefreshInterval()
    {
        EquityMarketDirectoryWorker.RetryWait(Retry, Refresh, 0).Should().Be(Retry);
        EquityMarketDirectoryWorker.RetryWait(Retry, Refresh, 1).Should().Be(Retry);
        EquityMarketDirectoryWorker.RetryWait(Retry, Refresh, 2).Should().Be(Retry * 2);
        EquityMarketDirectoryWorker.RetryWait(Retry, Refresh, 4).Should().Be(Retry * 8);
        EquityMarketDirectoryWorker
            .RetryWait(Retry, Refresh, 9)
            .Should()
            .Be(Refresh, "the wait never exceeds the interval the market would run at anyway");
        EquityMarketDirectoryWorker.RetryWait(Retry, Refresh, int.MaxValue).Should().Be(Refresh);
        EquityMarketDirectoryWorker
            .RetryWait(TimeSpan.FromHours(48), Refresh, 0)
            .Should()
            .Be(
                Refresh,
                "a retry interval longer than the refresh interval is the refresh interval"
            );
        EquityMarketDirectoryWorker.RetryWait(TimeSpan.Zero, Refresh, 3).Should().Be(Refresh);
        // A source that refuses its own data on the fourth try is left alone for two hours, not asked again
        // in fifteen minutes, because the capture that failed cost hundreds of requests.
        EquityMarketDirectoryWorker
            .IsDue(Row(null), Now.AddMinutes(-16), 4, Now, Refresh, Retry)
            .Should()
            .BeFalse();
        EquityMarketDirectoryWorker
            .IsDue(Row(null), Now.AddMinutes(-121), 4, Now, Refresh, Retry)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void ARefreshedMarket_RunsAgainOnlyWhenItsDirectoryIsOlderThanTheInterval()
    {
        EquityMarketDirectoryWorker
            .IsDue(Row(Now.AddHours(-23)), Now.AddHours(-23), 0, Now, Refresh, Retry)
            .Should()
            .BeFalse();
        EquityMarketDirectoryWorker
            .IsDue(Row(Now.AddHours(-25)), Now.AddHours(-25), 0, Now, Refresh, Retry)
            .Should()
            .BeTrue();
    }
}
