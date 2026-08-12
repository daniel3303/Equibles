using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Messaging.Attributes;
using Equibles.Messaging.Contracts.Holdings;
using FlexLabs.EntityFrameworkCore.Upsert;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService.Consumers;

/// <summary>
/// Marks the AUM snapshot for the affected ReportDate and its following 13F quarter dirty so
/// <see cref="AumSnapshotDrainWorker"/> rebuilds it after the cooldown
/// elapses. The expensive multi-distinct rebuild used to run inline here —
/// during 13F filing-season burst windows (Feb / May / Aug / Nov) hundreds
/// of imports per day for the same quarter triggered hundreds of redundant
/// rebuilds. The dirty flag coalesces those into one rebuild per cooldown
/// window.
///
/// Implemented as a single <c>INSERT … ON CONFLICT (ReportDate) DO UPDATE …</c>
/// (FlexLabs UpsertRange). For a brand-new quarter that has no snapshot row
/// yet, the row is inserted with <c>DirtyAt = UtcNow</c> and zero aggregates —
/// the dashboard sees an empty quarter for at most one drain cooldown until
/// the rebuild lands. For an existing row, the first ordinary event timestamp
/// is preserved so a filing burst cannot postpone the rebuild indefinitely.
/// A future-dated drain claim is replaced by the incoming event timestamp, so
/// an import during a rebuild stays scheduled for a follow-up refresh. Aggregate
/// columns are untouched. Doing both branches atomically removes
/// the consumer→AnyAsync→Rebuild TOCTOU race between parallel consumers.
/// </summary>
[Consumer]
public class Filings13FImportedConsumer : IConsumer<Filings13FImported>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<Filings13FImportedConsumer> _logger;

    public Filings13FImportedConsumer(
        IServiceScopeFactory scopeFactory,
        ILogger<Filings13FImportedConsumer> logger
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<Filings13FImported> context)
    {
        var reportDate = context.Message.ReportDate;
        var cancellationToken = context.CancellationToken;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();

        var nextReportDate = await dbContext
            .Set<AumQuarterlySnapshot>()
            .Where(snapshot => snapshot.ReportDate > reportDate)
            .OrderBy(snapshot => snapshot.ReportDate)
            .Select(snapshot => (DateOnly?)snapshot.ReportDate)
            .FirstOrDefaultAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var dirtySnapshots = new List<AumQuarterlySnapshot>
        {
            new()
            {
                ReportDate = reportDate,
                TotalValue = 0L,
                FilerCount = 0,
                PositionCount = 0,
                StockCount = 0,
                FilingCount = 0,
                // ComputedAt = DateTime.UtcNow by C# default, but the row is a
                // stub: the drain worker overwrites every column on rebuild.
                ComputedAt = now,
                DirtyAt = now,
            },
        };
        if (nextReportDate is { } following)
        {
            // StockQuarterlyActivity for a quarter embeds its prior-quarter columns. A late
            // amendment therefore invalidates both the amended quarter and its immediate
            // successor; marking the successor here prevents a permanently stale comparison.
            dirtySnapshots.Add(
                new AumQuarterlySnapshot
                {
                    ReportDate = following,
                    ComputedAt = now,
                    DirtyAt = now,
                }
            );
        }

        // Single atomic statement: INSERT a stub or retain the first ordinary event time.
        // A future DirtyAt is the drain's active claim lease; replace it so an import
        // during the rebuild cannot be cleared with that lease. Aggregate values and
        // ComputedAt on an existing row stay untouched.
        await dbContext
            .Set<AumQuarterlySnapshot>()
            .UpsertRange(dirtySnapshots)
            .On(s => s.ReportDate)
            .WhenMatched(
                (existing, incoming) =>
                    new AumQuarterlySnapshot
                    {
                        DirtyAt =
                            existing.DirtyAt == null || existing.DirtyAt > incoming.DirtyAt
                                ? incoming.DirtyAt
                                : existing.DirtyAt,
                    }
            )
            .RunAsync(cancellationToken);

        _logger.LogInformation(
            "Marked AUM snapshots dirty for {ReportDate} and {FollowingReportDate} ({FilingCount} filing(s))",
            reportDate,
            nextReportDate,
            context.Message.FilingCount
        );
    }
}
