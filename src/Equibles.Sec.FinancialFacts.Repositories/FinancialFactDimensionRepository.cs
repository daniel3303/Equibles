using Equibles.Data;
using Equibles.Sec.FinancialFacts.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.FinancialFacts.Repositories;

public class FinancialFactDimensionMember
{
    public virtual string Member { get; set; }
    public virtual string Axis { get; set; }
}

public class FinancialFactDimensionRepository : BaseRepository<FinancialFactDimension>
{
    public FinancialFactDimensionRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    /// <summary>
    /// Distinct member QNames for the requested axes after a database-ordered keyset cursor.
    /// PostgreSQL's ordinary DISTINCT plan scans every repeated fact row, so one recursive
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
            return GetAll()
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
                    FROM "FinancialFactDimension" AS "dimension"
                    WHERE "dimension"."Axis" = "seed"."Axis"
                        AND "dimension"."Member" > {afterMember}
                    ORDER BY "dimension"."Member"
                    LIMIT 1
                ), 1
                FROM unnest({axes}) AS "seed" ("Axis")
                UNION ALL
                SELECT "members"."Axis", (
                    SELECT "dimension"."Member"
                    FROM "FinancialFactDimension" AS "dimension"
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
                FROM "FinancialFactDimension" AS "dimension"
                WHERE "dimension"."Axis" = ANY({axes})
                    AND "dimension"."Member" = "page"."Member"
            ) AS "Axis"
            FROM "page"
            ORDER BY "page"."Member"
            """
        );
    }
}
