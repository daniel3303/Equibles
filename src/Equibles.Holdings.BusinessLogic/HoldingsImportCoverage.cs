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
    RealtimeSweepStateRepository states,
    HoldingsCusipRescanRepository cusipRescans
)
{
    /// <summary>The cursor of the realtime 13F sweep.</summary>
    public const string SweepWorkerName = "Holdings13FRealtime";

    /// <summary>The cursor of the discovery pass that reads the newest days ahead of a rewound sweep.</summary>
    public const string DiscoveryWorkerName = "Holdings13FDiscovery";

    private static readonly string[] RecentFilingWorkers = [SweepWorkerName, DiscoveryWorkerName];

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
        if (
            replayPending
            || await cusipRescans
                .GetAll()
                .AnyAsync(row => row.CompletedAt == null, cancellationToken)
        )
            return "Institutional holdings are being reconciled with SEC filings. Holder totals and changes may be incomplete until reconciliation finishes.";
        // A replay rewinds the sweep while discovery keeps reading the newest days, so recent
        // filings are behind only when neither cursor is near today.
        var sweptThrough = await states
            .GetAll()
            .Where(row => RecentFilingWorkers.Contains(row.WorkerName))
            .MaxAsync(row => (DateOnly?)row.SweptThrough, cancellationToken);
        if (
            sweptThrough == null
            || sweptThrough < DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-2)
        )
            return "Recent SEC filings are still being reconciled. Holder totals and changes may be incomplete until processing catches up.";
        // A filing whose period never parsed can only report a quarter that ended before it was
        // filed, so it leaves later quarters complete; one with no parsed filing date counts for all.
        var unresolved = await failures
            .GetWithheld()
            .CountAsync(
                row =>
                    (
                        reportDate == default
                        || (
                            row.ReportDate == null
                            && (row.FilingDate >= reportDate || row.FilingDate == default)
                        )
                        || row.ReportDate == reportDate
                    ),
                cancellationToken
            );
        return unresolved == 0
            ? null
            : "Some SEC filings for this period have not finished importing. Holder totals and changes may be incomplete; these filings remain queued for recovery.";
    }
}
