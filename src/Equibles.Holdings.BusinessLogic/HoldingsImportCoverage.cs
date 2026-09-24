using Equibles.Core.AutoWiring;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.BusinessLogic;

/// <summary>Operational completeness is separate from the configured history boundary.</summary>
[Service]
public class HoldingsImportCoverage(
    HoldingsImportFailureRepository failures,
    ProcessedDataSetRepository dataSets,
    RealtimeSweepStateRepository states
)
{
    public async Task<string> GetIncompleteReason(
        DateOnly reportDate,
        CancellationToken cancellationToken = default
    )
    {
        var replayPending = await dataSets
            .GetAll()
            .AnyAsync(
                row =>
                    row.FileName == ProcessedDataSet.RescanPendingFileName
                    || row.FileName == ProcessedDataSet.RealtimeReplayPendingFileName
                    || row.FileName == ProcessedDataSet.CoverageAuditPendingFileName,
                cancellationToken
            );
        if (replayPending)
            return "Institutional holdings are being reconciled with SEC filings. Holder totals and changes may be incomplete until reconciliation finishes.";
        var sweptThrough = await states
            .GetByWorker("Holdings13FRealtime")
            .Select(row => (DateOnly?)row.SweptThrough)
            .SingleOrDefaultAsync(cancellationToken);
        if (
            sweptThrough == null
            || sweptThrough < DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-2)
        )
            return "Recent SEC filings are still being reconciled. Holder totals and changes may be incomplete until processing catches up.";
        var unresolved = await failures
            .GetAll()
            .CountAsync(
                row =>
                    row.ResolvedAt == null
                    && (
                        reportDate == default
                        || row.ReportDate == null
                        || row.ReportDate == reportDate
                    ),
                cancellationToken
            );
        return unresolved == 0
            ? null
            : "Some SEC filings for this period have not finished importing. Holder totals and changes may be incomplete; these filings remain queued for recovery.";
    }
}
