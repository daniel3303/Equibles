using Equibles.Yahoo.HostedService.Services;

namespace Equibles.UnitTests.Yahoo;

/// <summary>
/// Pins the persisted Yahoo-enrichment cadence and bounded oldest-first selection. The worker may
/// restart between any two batches; completed attempts must leave the head while untouched stocks
/// continue through the batch frontier.
/// </summary>
public class YahooPriceImportServiceEnrichmentBatchTests
{
    private static readonly DateTime Now = new(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    [Fact]
    public void SelectEnrichmentBatch_ChoosesNeverAttemptedThenOldestDuePrimaryStocks()
    {
        var neverAttempted = Target("NEVER", null);
        var oldest = Target("OLDEST", Now.AddDays(-3));
        var due = Target("DUE", Now.Subtract(Interval));
        var current = Target("CURRENT", Now.AddHours(-2));
        var secondary = Target("SECONDARY", null, isPrimary: false);

        var selection = YahooPriceImportService.SelectEnrichmentBatch(
            [current, due, secondary, oldest, neverAttempted],
            Now,
            Interval,
            batchSize: 2
        );

        selection.Targets.Select(target => target.Ticker).Should().Equal("NEVER", "OLDEST");
        selection.Remaining.Should().Be(1);
    }

    [Fact]
    public void SelectEnrichmentBatch_AfterCheckpointAdvance_ResumesAtNextDueStock()
    {
        var completed = Target("AAPL", Now.AddMinutes(-1));
        var next = Target("MSFT", null);

        var selection = YahooPriceImportService.SelectEnrichmentBatch(
            [completed, next],
            Now,
            Interval,
            batchSize: 1
        );

        selection.Targets.Should().ContainSingle().Which.Ticker.Should().Be("MSFT");
        selection.Remaining.Should().Be(0);
    }

    [Fact]
    public void SelectEnrichmentBatch_NonPositiveBatchSize_StillMakesProgress()
    {
        var selection = YahooPriceImportService.SelectEnrichmentBatch(
            [Target("AAPL", null), Target("MSFT", null)],
            Now,
            Interval,
            batchSize: 0
        );

        selection.Targets.Should().ContainSingle();
        selection.Remaining.Should().Be(1);
    }

    private static PriceSeriesTarget Target(
        string ticker,
        DateTime? attemptedAt,
        bool isPrimary = true
    ) => new(ticker, Guid.NewGuid(), IsPrimary: isPrimary, YahooEnrichmentAttemptedAt: attemptedAt);
}
