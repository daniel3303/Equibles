using Equibles.Core.Configuration;
using Equibles.GovernmentContracts.HostedService.Services;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.GovernmentContracts;

// Contract: the incremental import cursor resumes the day after the newest credible
// action date and can never point past today — a single mis-dated future row froze
// prod ingestion for over two years when the cursor keyed on raw max(ActionDate).
public class GovernmentContractsImportServiceCursorTests
{
    private static readonly DateOnly Today = new(2026, 7, 17);

    [Fact]
    public void Resumes_the_day_after_the_latest_action_date()
    {
        var start = GovernmentContractsImportService.ResolveStartDate(
            new DateOnly(2026, 6, 1),
            Today,
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
            Today,
            new WorkerOptions()
        );

        start.Should().Be(Today.AddDays(1));
    }

    [Fact]
    public void Clamps_date_only_max_value_without_overflowing()
    {
        var start = GovernmentContractsImportService.ResolveStartDate(
            DateOnly.MaxValue,
            Today,
            new WorkerOptions()
        );

        start.Should().Be(Today.AddDays(1));
    }

    [Fact]
    public void Falls_back_to_the_configured_min_sync_date_when_the_table_is_empty()
    {
        var start = GovernmentContractsImportService.ResolveStartDate(
            null,
            Today,
            new WorkerOptions { MinSyncDate = new DateTime(2021, 3, 1) }
        );

        start.Should().Be(new DateOnly(2021, 3, 1));
    }

    [Fact]
    public void Falls_back_to_the_default_floor_when_the_table_is_empty_and_unconfigured()
    {
        var start = GovernmentContractsImportService.ResolveStartDate(
            null,
            Today,
            new WorkerOptions()
        );

        start.Should().Be(new DateOnly(2020, 1, 1));
    }

    [Fact]
    public void An_action_date_of_today_resumes_tomorrow()
    {
        var start = GovernmentContractsImportService.ResolveStartDate(
            Today,
            Today,
            new WorkerOptions()
        );

        start.Should().Be(Today.AddDays(1));
    }
}
