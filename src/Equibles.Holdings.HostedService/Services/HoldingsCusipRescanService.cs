using System.Data;
using Equibles.Core.AutoWiring;
using Equibles.Holdings.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService.Services;

[Service]
public class HoldingsCusipRescanService(
    HoldingsCusipRescanRepository requests,
    ProcessedDataSetRepository processed,
    HoldingsDataSetClient dataSets,
    HoldingsImportFailureRepository failures,
    HoldingsRescanSignal signal,
    HoldingsRealtimeReplaySignal realtimeSignal,
    ILogger<HoldingsCusipRescanService> logger
)
{
    public async Task Scan(DateOnly minReportDate, CancellationToken cancellationToken)
    {
        // Snapshot this batch: new events never reset an in-flight archive cursor.
        var pending = await requests
            .GetAll()
            .Where(row => row.CompletedAt == null)
            .OrderBy(row => row.RequestedAt)
            .Take(1000)
            .ToListAsync(cancellationToken);
        if (pending.Count == 0)
            return;
        var names = await processed
            .GetAll()
            .Select(row => row.FileName)
            .ToListAsync(cancellationToken);
        var archives = names
            .Select(name => new
            {
                Name = name,
                End = Holdings13FRealtimeWorker.ParseDataSetEndDate(name),
            })
            .Where(row => row.End >= minReportDate)
            .OrderBy(row => row.End)
            .ToList();
        foreach (var archive in archives)
        {
            var batch = pending
                .Where(row => row.ScannedThrough == null || row.ScannedThrough < archive.End)
                .ToList();
            if (batch.Count == 0)
                continue;
            var cusips = batch
                .SelectMany(row => new[] { row.PreviousCusip, row.Cusip })
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            using var source = await dataSets.DownloadDataSet(archive.Name, cancellationToken);
            var affected = await HoldingsCusipArchiveScanner.FindAffectedFilers(
                source,
                cusips,
                minReportDate,
                cancellationToken
            );
            await using var transaction = await requests.CreateTransaction(
                IsolationLevel.ReadCommitted,
                cancellationToken
            );
            foreach (var filing in affected)
                await failures.EnqueueRecovery(
                    filing.AccessionNumber,
                    filing.Cik,
                    filing.FilingDate,
                    cancellationToken,
                    supersedesActiveAttempt: true
                );
            foreach (var request in batch)
                request.ScannedThrough = archive.End;
            await requests.SaveChanges();
            await transaction.CommitAsync(cancellationToken);
            if (affected.Count != 0)
                realtimeSignal.RequestReplay();
            logger.LogInformation(
                "CUSIP rescan checked {Archive} and queued {Count} affected filers",
                archive.Name,
                affected.Count
            );
        }

        // The unpublished tail has no bulk source yet. Preserve the existing ordered
        // realtime replay for that window, without invalidating any quarterly markers.
        await using (
            var transaction = await requests.CreateTransaction(
                IsolationLevel.ReadCommitted,
                cancellationToken
            )
        )
        {
            await processed.QueueRealtimeReplay(cancellationToken);
            foreach (var request in pending)
                request.CompletedAt = DateTime.UtcNow;
            await requests.SaveChanges();
            await transaction.CommitAsync(cancellationToken);
        }
        if (await requests.GetAll().AnyAsync(row => row.CompletedAt == null, cancellationToken))
            signal.RequestRescan();
    }
}
