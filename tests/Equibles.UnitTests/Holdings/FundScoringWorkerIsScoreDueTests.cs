using Equibles.Holdings.HostedService;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Pins the incremental re-score rule in <see cref="FundScoringWorker"/>: unscored filers are
/// always due; scored filers are due only when new holdings data was imported after the score
/// or the score has aged past the staleness floor. The daily full-universe recompute this rule
/// replaced was the largest database drain on record, so a regression here silently reverts to
/// ~15k multi-year backtests per day.
/// </summary>
public class FundScoringWorkerIsScoreDueTests
{
    private static readonly DateTime StaleBefore = new(2026, 6, 26, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void UnscoredFiler_IsDue()
    {
        FundScoringWorker
            .IsScoreDue(null, StaleBefore.AddDays(-30), StaleBefore)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void FreshScore_NoNewData_IsNotDue()
    {
        var lastScored = StaleBefore.AddDays(2);

        FundScoringWorker
            .IsScoreDue(lastScored, lastScored.AddDays(-10), StaleBefore)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void FreshScore_DataImportedAfterScore_IsDue()
    {
        var lastScored = StaleBefore.AddDays(2);

        FundScoringWorker
            .IsScoreDue(lastScored, lastScored.AddMinutes(1), StaleBefore)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void StaleScore_NoNewData_IsDue()
    {
        var lastScored = StaleBefore.AddDays(-1);

        FundScoringWorker
            .IsScoreDue(lastScored, lastScored.AddDays(-10), StaleBefore)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void DataImportedExactlyAtScoreTime_IsNotDue()
    {
        // The importer stamps CreationTime before the score reads it, so an equal timestamp
        // means the score already saw that data — only strictly newer imports re-trigger.
        var lastScored = StaleBefore.AddDays(2);

        FundScoringWorker.IsScoreDue(lastScored, lastScored, StaleBefore).Should().BeFalse();
    }
}
