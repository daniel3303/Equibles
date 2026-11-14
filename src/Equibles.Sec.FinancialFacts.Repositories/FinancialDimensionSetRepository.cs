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
    /// Distinct member QNames for the requested axes after a database-ordered keyset cursor.
    /// PostgreSQL's ordinary DISTINCT plan scans every row of an axis, so one recursive
    /// loose-index scan per axis jumps directly between values in the (Axis, Member) index.
    /// The final merge stays in PostgreSQL so its ordering exactly matches the cursor predicate.
    /// </summary>
    public IQueryable<FinancialFactDimensionMember> GetDistinctMembersPage(
        string[] axes,
        string afterMember,
        int take
    )
    {
        if (!DbContext.Database.IsRelational())
        {
            return DbContext
                .Set<FinancialDimensionSetMember>()
                .Where(d => axes.Contains(d.Axis) && string.Compare(d.Member, afterMember) > 0)
                .GroupBy(d => d.Member)
                .Select(group => new FinancialFactDimensionMember
                {
                    Member = group.Key,
                    Axis = group.Min(d => d.Axis),
                })
                .OrderBy(candidate => candidate.Member)
                .Take(take);
        }

        return DbContext.Database.SqlQuery<FinancialFactDimensionMember>(
            $"""
            WITH RECURSIVE "members" ("Axis", "Member", "Depth") AS (
                SELECT "seed"."Axis", (
                    SELECT "dimension"."Member"
                    FROM "FinancialDimensionSetMember" AS "dimension"
                    WHERE "dimension"."Axis" = "seed"."Axis"
                        AND "dimension"."Member" > {afterMember}
                    ORDER BY "dimension"."Member"
                    LIMIT 1
                ), 1
                FROM unnest({axes}) AS "seed" ("Axis")
                UNION ALL
                SELECT "members"."Axis", (
                    SELECT "dimension"."Member"
                    FROM "FinancialDimensionSetMember" AS "dimension"
                    WHERE "dimension"."Axis" = "members"."Axis"
                        AND "dimension"."Member" > "members"."Member"
                    ORDER BY "dimension"."Member"
                    LIMIT 1
                ), "members"."Depth" + 1
                FROM "members"
                WHERE "members"."Member" IS NOT NULL AND "members"."Depth" < {take}
            ), "page" AS (
                SELECT "Member"
                FROM "members"
                WHERE "Member" IS NOT NULL
                GROUP BY "Member"
                ORDER BY "Member"
                LIMIT {take}
            )
            SELECT "page"."Member", (
                SELECT MIN("dimension"."Axis")
                FROM "FinancialDimensionSetMember" AS "dimension"
                WHERE "dimension"."Axis" = ANY({axes})
                    AND "dimension"."Member" = "page"."Member"
            ) AS "Axis"
            FROM "page"
            ORDER BY "page"."Member"
            """
        );
    }

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
