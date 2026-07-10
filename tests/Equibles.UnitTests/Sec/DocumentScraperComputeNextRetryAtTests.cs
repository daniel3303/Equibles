using Equibles.Sec.HostedService;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins the tombstone backoff schedule: doubling from one day, capped at 30 —
/// retries never stop, so a permanently poisonous filing costs at most one
/// download a month while a later parser fix still eventually ingests it.
/// </summary>
public class DocumentScraperComputeNextRetryAtTests
{
    private static readonly DateTime Now = new(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(5, 16)]
    [InlineData(6, 30)]
    [InlineData(50, 30)]
    public void BackoffDoublesAndCapsAtThirtyDays(int attemptCount, int expectedDays)
    {
        DocumentScraper
            .ComputeNextRetryAt(attemptCount, Now)
            .Should()
            .Be(Now.AddDays(expectedDays));
    }

    [Fact]
    public void ZeroAttempts_TreatedAsFirst()
    {
        DocumentScraper.ComputeNextRetryAt(0, Now).Should().Be(Now.AddDays(1));
    }
}
