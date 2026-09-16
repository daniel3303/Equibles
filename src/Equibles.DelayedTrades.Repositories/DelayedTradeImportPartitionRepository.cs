using Equibles.Data;
using Equibles.DelayedTrades.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.DelayedTrades.Repositories;

public class DelayedTradeImportPartitionRepository(EquiblesFinancialDbContext dbContext)
    : BaseRepository<DelayedTradeImportPartition>(dbContext)
{
    public Task<DelayedTradeImportPartition> GetMarker(
        string dataset,
        DateOnly partitionDate,
        string scopeKey,
        CancellationToken cancellationToken = default
    ) =>
        GetAll()
            .SingleOrDefaultAsync(
                row =>
                    row.Dataset == dataset
                    && row.PartitionDate == partitionDate
                    && row.ScopeKey == scopeKey,
                cancellationToken
            );

    public IQueryable<DelayedTradeImportPartition> GetByScope(string dataset, string scopeKey) =>
        GetAll().Where(row => row.Dataset == dataset && row.ScopeKey == scopeKey);

    public Task<DelayedTradeImportPartition> GetLatestMarker(
        string dataset,
        string scopeKey,
        CancellationToken cancellationToken = default
    ) =>
        GetByScope(dataset, scopeKey)
            .OrderByDescending(row => row.PartitionDate)
            .FirstOrDefaultAsync(cancellationToken);
}
