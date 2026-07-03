using Equibles.Holdings.HostedService;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Pins the incremental re-score rule in <see cref="FundScoringWorker"/>: unscored filers are
/// always due; scored filers are due only when new holdings data was imported after the score,
/// when the latest filing is dated on or after the score's day (in-place amendment
/// restatements rewrite rows without a new CreationTime), or when the score has aged past the
/// staleness floor. The daily full-universe recompute this rule replaced was the largest
/// database drain on record, so a regression here silently reverts to ~15k multi-year
/// backtests per day.
/// </summary>
public class FundScoringWorkerIsScoreDueTests
{
    private static readonly DateTime StaleBefore = new(2026, 6, 26, 12, 0, 0, DateTimeKind.Utc);

    // A latest-filing date far enough in the past that the filing-date clause never fires;
    // each test overrides it when the clause itself is under test.
    private static readonly DateOnly OldFiling = new(2026, 1, 15);

    [Fact]
    public void UnscoredFiler_IsDue()
    {
        FundScoringWorker
            .IsScoreDue(null, StaleBefore.AddDays(-30), OldFiling, StaleBefore)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void FreshScore_NoNewData_IsNotDue()
    {
        var lastScored = StaleBefore.AddDays(2);

        FundScoringWorker
            .IsScoreDue(lastScored, lastScored.AddDays(-10), OldFiling, StaleBefore)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void FreshScore_DataImportedAfterScore_IsDue()
    {
        var lastScored = StaleBefore.AddDays(2);

        FundScoringWorker
            .IsScoreDue(lastScored, lastScored.AddMinutes(1), OldFiling, StaleBefore)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void FreshScore_FilingDatedOnScoreDay_IsDue()
    {
        // An amendment restating the same quarter updates rows in place — no new
        // CreationTime — so the filing date is the only change signal. A filing dated the
        // same day as the score must re-trigger: the restatement may have been imported
        // after the score ran.
        var lastScored = StaleBefore.AddDays(2);
        var filedOn = DateOnly.FromDateTime(lastScored);

        FundScoringWorker
            .IsScoreDue(lastScored, lastScored.AddDays(-10), filedOn, StaleBefore)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void FreshScore_FilingDatedBeforeScoreDay_IsNotDue()
    {
        var lastScored = StaleBefore.AddDays(2);
        var filedOn = DateOnly.FromDateTime(lastScored).AddDays(-1);

        FundScoringWorker
            .IsScoreDue(lastScored, lastScored.AddDays(-10), filedOn, StaleBefore)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void StaleScore_NoNewData_IsDue()
    {
        var lastScored = StaleBefore.AddDays(-1);

        FundScoringWorker
            .IsScoreDue(lastScored, lastScored.AddDays(-10), OldFiling, StaleBefore)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void DataImportedExactlyAtScoreTime_IsNotDue()
    {
        // The importer stamps CreationTime before the score reads it, so an equal timestamp
        // means the score already saw that data — only strictly newer imports re-trigger.
        var lastScored = StaleBefore.AddDays(2);

        FundScoringWorker
            .IsScoreDue(lastScored, lastScored, OldFiling, StaleBefore)
            .Should()
            .BeFalse();
    }
}
