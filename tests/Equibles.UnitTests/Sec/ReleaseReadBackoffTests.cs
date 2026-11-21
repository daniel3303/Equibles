using Equibles.Sec.FinancialFacts.HostedService;

namespace Equibles.UnitTests.Sec;

public class ReleaseReadBackoffTests
{
    private static readonly DateTime Now = new(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);
    private readonly Guid _release = Guid.NewGuid();
    private readonly ReleaseReadBackoff _backoff = new();

    [Fact]
    public void Waiting_AfterAFailure_HoldsTheReleaseForAnHourThenOffersItAgain()
    {
        _backoff.Failed(_release, Now);

        _backoff.Waiting(Now.AddMinutes(59)).Should().Equal(_release);
        _backoff.Waiting(Now.AddHours(1)).Should().BeEmpty();
    }

    [Fact]
    public void Waiting_AfterRepeatedFailures_WaitsLongerEachTimeUpToADay()
    {
        _backoff.Failed(_release, Now);
        _backoff.Failed(_release, Now);

        _backoff.Waiting(Now.AddMinutes(119)).Should().Equal(_release);
        _backoff.Waiting(Now.AddHours(2)).Should().BeEmpty();

        for (var i = 0; i < 40; i++)
            _backoff.Failed(_release, Now);

        _backoff.Waiting(Now.AddHours(23)).Should().Equal(_release);
        _backoff.Waiting(Now.AddHours(24)).Should().BeEmpty();
    }

    [Fact]
    public void Succeeded_AfterAFailure_StartsTheNextFailureFromAnHourAgain()
    {
        _backoff.Failed(_release, Now);
        _backoff.Failed(_release, Now);
        _backoff.Succeeded(_release);

        _backoff.Failed(_release, Now);

        _backoff.Waiting(Now.AddHours(1)).Should().BeEmpty();
    }
}
