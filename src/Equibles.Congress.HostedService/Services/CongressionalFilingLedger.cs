using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Models;
using Equibles.Congress.Repositories;
using Equibles.Core.AutoWiring;
using FlexLabs.EntityFrameworkCore.Upsert;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Congress.HostedService.Services;

/// <summary>
/// The ingested-filing ledger behind the congressional sync services. Every
/// cycle loads the already-ingested source ids per filing kind so the clients
/// skip re-downloading those filings, and records the newly handled ones only
/// after the cycle's data has been committed — a failed cycle therefore
/// re-fetches its filings instead of losing them.
/// </summary>
[Service]
public class CongressionalFilingLedger
{
    private readonly IServiceScopeFactory _scopeFactory;

    public CongressionalFilingLedger(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public virtual async Task<IReadOnlySet<string>> GetProcessedSourceIds(
        CongressionalFilingKind kind,
        CancellationToken ct
    )
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repository =
            scope.ServiceProvider.GetRequiredService<CongressionalFilingRecordRepository>();
        var sourceIds = await repository.GetByKind(kind).Select(r => r.SourceId).ToListAsync(ct);
        return sourceIds.ToHashSet();
    }

    public virtual async Task RecordProcessed(
        CongressionalFilingKind kind,
        IReadOnlyCollection<ProcessedFiling> filings,
        CancellationToken ct
    )
    {
        if (filings.Count == 0)
            return;

        // Dedupe by source id: an id repeated in one batch (e.g. a listing
        // page boundary shifting mid-search) would make the upsert's ON
        // CONFLICT hit the same row twice and abort the whole statement.
        var records = filings
            .GroupBy(f => f.SourceId)
            .Select(g => g.First())
            .Select(f => new CongressionalFilingRecord
            {
                Kind = kind,
                SourceId = f.SourceId,
                FilingDate = f.FilingDate,
                ItemCount = f.ItemCount,
            })
            .ToList();

        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        await dbContext
            .Set<CongressionalFilingRecord>()
            .UpsertRange(records)
            .On(r => new { r.Kind, r.SourceId })
            .NoUpdate()
            .RunAsync(ct);
    }
}
