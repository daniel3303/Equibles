using Equibles.Core.Configuration;
using Equibles.GovernmentContracts.HostedService.Services;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.GovernmentContracts;

// Contract: with no persisted checkpoint the import cursor resumes the day after the newest
// credible action date and can never point past today — a single mis-dated future row froze
// prod ingestion for over two years when the cursor keyed on raw max(ActionDate).
//
// Once a scan checkpoint exists it OWNS the cursor: the scan resumes after the last
// fully-completed window (so a transport abort no longer restarts the whole range and
// re-floods the Errors page), never behind data already ingested, and re-covers a trailing
// lookback window so awards USAspending publishes late are not permanently skipped.
public class GovernmentContractsImportServiceCursorTests
{
    private static readonly DateOnly Today = new(2026, 7, 17);
    private const int Lookback = 7;

    // --- No checkpoint yet: unchanged data-derived watermark behavior ---

    [Fact]
    public void Resumes_the_day_after_the_latest_action_date()
    {
        var start = GovernmentContractsImportService.ResolveStartDate(
            new DateOnly(2026, 6, 1),
            checkpointEnd: null,
            Today,
            Lookback,
            new WorkerOptions()
        );

        start.Should().Be(new DateOnly(2026, 6, 2));
    }

    [Fact]
    public void Clamps_a_future_action_date_to_today_instead_of_freezing()
    {
        // Regression: a 2028-12-31 outlier must not push the cursor into 2029 — the
        // worst allowed effect is "synced through today" (resume tomorrow).
        var start = GovernmentContractsImportService.ResolveStartDate(
            new DateOnly(2028, 12, 31),
            checkpointEnd: null,
            Today,
            Lookback,
            new WorkerOptions()
        );

        start.Should().Be(Today.AddDays(1));
    }

    [Fact]
    public void Clamps_date_only_max_value_without_overflowing()
    {
        var start = GovernmentContractsImportService.ResolveStartDate(
            DateOnly.MaxValue,
            checkpointEnd: null,
            Today,
            Lookback,
            new WorkerOptions()
        );

        start.Should().Be(Today.AddDays(1));
    }

    [Fact]
    public void Falls_back_to_the_configured_min_sync_date_when_the_table_is_empty()
    {
        var start = GovernmentContractsImportService.ResolveStartDate(
            null,
            checkpointEnd: null,
            Today,
            Lookback,
            new WorkerOptions { MinSyncDate = new DateTime(2021, 3, 1) }
        );

        start.Should().Be(new DateOnly(2021, 3, 1));
    }

    [Fact]
    public void Falls_back_to_the_default_floor_when_the_table_is_empty_and_unconfigured()
    {
        var start = GovernmentContractsImportService.ResolveStartDate(
            null,
            checkpointEnd: null,
            Today,
            Lookback,
            new WorkerOptions()
        );

        start.Should().Be(new DateOnly(2020, 1, 1));
    }

    [Fact]
    public void An_action_date_of_today_resumes_tomorrow()
    {
        var start = GovernmentContractsImportService.ResolveStartDate(
            Today,
            checkpointEnd: null,
            Today,
            Lookback,
            new WorkerOptions()
        );

        start.Should().Be(Today.AddDays(1));
    }

    // --- Checkpoint present: it owns the cursor ---

    [Fact]
    public void A_backfill_checkpoint_resumes_the_day_after_the_completed_window()
    {
        // The freeze fix: mid-backfill (checkpoint far behind today) the scan resumes right
        // after the last completed window, NOT at the stale max(ActionDate) that a transport
        // abort left unchanged.
        var start = GovernmentContractsImportService.ResolveStartDate(
            latestActionDate: new DateOnly(2022, 1, 13),
            checkpointEnd: new DateOnly(2022, 1, 20),
            Today,
            Lookback,
            new WorkerOptions()
        );

        start.Should().Be(new DateOnly(2022, 1, 21));
    }

    [Fact]
    public void A_checkpoint_carries_the_scan_forward_past_a_lagging_watermark()
    {
        // Even with no rows inserted past 2022-01-13 (every window since was empty or matched
        // no public company), the checkpoint carries the scan forward on its own.
        var start = GovernmentContractsImportService.ResolveStartDate(
            latestActionDate: new DateOnly(2022, 1, 13),
            checkpointEnd: new DateOnly(2023, 6, 30),
            Today,
            Lookback,
            new WorkerOptions()
        );

        start.Should().Be(new DateOnly(2023, 7, 1));
    }

    [Fact]
    public void A_checkpoint_behind_ingested_data_never_resumes_behind_the_watermark()
    {
        // Defensive: the checkpoint should always lead the watermark, but if it somehow lags,
        // resume after the newer ingested data rather than re-scanning already-stored rows.
        var start = GovernmentContractsImportService.ResolveStartDate(
            latestActionDate: new DateOnly(2022, 1, 20),
            checkpointEnd: new DateOnly(2022, 1, 10),
            Today,
            Lookback,
            new WorkerOptions()
        );

        start.Should().Be(new DateOnly(2022, 1, 21));
    }

    [Fact]
    public void A_caught_up_checkpoint_re_scans_the_trailing_lookback_window()
    {
        // Once the checkpoint reaches today, each cycle re-covers the trailing lookback window
        // so late-published awards inside it are still picked up (deduplicated on insert).
        var start = GovernmentContractsImportService.ResolveStartDate(
            latestActionDate: Today,
            checkpointEnd: Today,
            Today,
            Lookback,
            new WorkerOptions()
        );

        start.Should().Be(Today.AddDays(-(Lookback - 1)));
    }

    [Fact]
    public void A_checkpoint_inside_the_lookback_window_pulls_back_to_the_floor()
    {
        // Checkpoint three days behind today with a 7-day lookback: start pulls back to the
        // lookback floor (today-6), re-covering the already-scanned tail and closing the gap.
        var start = GovernmentContractsImportService.ResolveStartDate(
            latestActionDate: Today.AddDays(-3),
            checkpointEnd: Today.AddDays(-3),
            Today,
            Lookback,
            new WorkerOptions()
        );

        start.Should().Be(Today.AddDays(-(Lookback - 1)));
    }

    [Fact]
    public void The_lookback_window_width_is_configurable()
    {
        var start = GovernmentContractsImportService.ResolveStartDate(
            latestActionDate: Today,
            checkpointEnd: Today,
            Today,
            rescanLookbackDays: 3,
            new WorkerOptions()
        );

        start.Should().Be(Today.AddDays(-2));
    }
}
