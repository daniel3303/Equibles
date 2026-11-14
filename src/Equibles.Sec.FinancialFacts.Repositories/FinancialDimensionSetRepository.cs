using Equibles.Data;
using Equibles.Sec.FinancialFacts.Data.Models;
using FlexLabs.EntityFrameworkCore.Upsert;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.FinancialFacts.Repositories;

public class FinancialDimensionSetRepository : BaseRepository<FinancialDimensionSet>
{
    private const int InsertBatchSize = 1000;

    public FinancialDimensionSetRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    /// <summary>
    /// Inserts the sets with their members in one transaction and leaves a set another writer
    /// stored first untouched, so a stored set always holds all of its members.
    /// </summary>
    public async Task AddIgnoringExisting(
        IReadOnlyCollection<FinancialDimensionSet> sets,
        CancellationToken cancellationToken = default
    )
    {
        // Key order on every writer, so two of them adding the same sets never wait in a cycle.
        var ordered = sets.OrderBy(set => set.Key, StringComparer.Ordinal).ToList();
        var members = ordered
            .SelectMany(set =>
                set.Members.OrderBy(member => member.Axis, StringComparer.Ordinal)
                    .ThenBy(member => member.Member, StringComparer.Ordinal)
            )
            .ToList();

        // The InMemory provider has no transactions and refuses to open one.
        await using var transaction =
            DbContext.Database.IsRelational() && DbContext.Database.CurrentTransaction == null
                ? await DbContext.Database.BeginTransactionAsync(cancellationToken)
                : null;
        foreach (var batch in ordered.Chunk(InsertBatchSize))
        {
            await GetDbSet()
                .UpsertRange(batch)
                .On(set => set.Key)
                .NoUpdate()
                .RunAsync(cancellationToken);
        }
        foreach (var batch in members.Chunk(InsertBatchSize))
        {
            await DbContext
                .Set<FinancialDimensionSetMember>()
                .UpsertRange(batch)
                .On(member => new
                {
                    member.DimensionSetKey,
                    member.Axis,
                    member.Member,
                })
                .NoUpdate()
                .RunAsync(cancellationToken);
        }
        if (transaction != null)
            await transaction.CommitAsync(cancellationToken);
    }
}
