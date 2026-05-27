using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Messaging.Attributes;
using Equibles.Messaging.Contracts.Holdings;
using FlexLabs.EntityFrameworkCore.Upsert;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService.Consumers;

/// <summary>
/// Marks the AUM snapshot for the affected ReportDate dirty so
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
/// the rebuild lands. For an existing row, <c>DirtyAt</c> is set only when
/// currently null (preserving the first event's timestamp so the drain
/// schedules the rebuild from the start of the burst, not the latest event);
/// aggregate columns are untouched. Doing both branches atomically removes
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

        var now = DateTime.UtcNow;
        var stub = new AumQuarterlySnapshot
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
        };

        // Single atomic statement: INSERT new stub OR set DirtyAt only when
        // currently null. WhenMatched assignments overwrite only the listed
        // columns, so aggregate values and ComputedAt on an existing row are
        // untouched (rebuilds, not the consumer, set those).
        await dbContext
            .Set<AumQuarterlySnapshot>()
            .UpsertRange(stub)
            .On(s => s.ReportDate)
            .WhenMatched(
                (existing, incoming) =>
                    new AumQuarterlySnapshot { DirtyAt = existing.DirtyAt ?? incoming.DirtyAt }
            )
            .RunAsync(cancellationToken);

        _logger.LogInformation(
            "Marked AUM snapshot dirty for {ReportDate} ({FilingCount} filing(s))",
            reportDate,
            context.Message.FilingCount
        );
    }
}
