using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Repositories;

public class RealtimeSweepStateRepository : BaseRepository<RealtimeSweepState>
{
    public RealtimeSweepStateRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<RealtimeSweepState> GetByWorker(string workerName)
    {
        return GetAll().Where(s => s.WorkerName == workerName);
    }

    public Task<RealtimeSweepState> Lock(Guid id, CancellationToken cancellationToken) =>
        GetDbSet()
            .FromSqlInterpolated(
                $"SELECT * FROM \"RealtimeSweepState\" WHERE \"Id\" = {id} FOR UPDATE"
            )
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

    public Task<int> Rewind(
        string workerName,
        DateOnly from,
        CancellationToken cancellationToken
    ) =>
        DbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "RealtimeSweepState" ("Id", "WorkerName", "SweptThrough", "UpdatedAt")
            VALUES ({Guid.NewGuid()}, {workerName}, {from}, clock_timestamp())
            ON CONFLICT ("WorkerName") DO UPDATE
            SET "SweptThrough" = LEAST("RealtimeSweepState"."SweptThrough", EXCLUDED."SweptThrough"),
                "UpdatedAt" = clock_timestamp()
            """,
            cancellationToken
        );

    // A sweep started before an identity reset must not acknowledge the reset's work.
    public Task<int> SaveProgress(
        string workerName,
        RealtimeSweepState expected,
        DateOnly sweptThrough,
        CancellationToken cancellationToken
    ) =>
        expected == null
            ? DbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO "RealtimeSweepState" ("Id", "WorkerName", "SweptThrough", "UpdatedAt")
                VALUES ({Guid.NewGuid()}, {workerName}, {sweptThrough}, clock_timestamp())
                ON CONFLICT ("WorkerName") DO NOTHING
                """,
                cancellationToken
            )
            : GetAll()
                .Where(s =>
                    s.Id == expected.Id
                    && s.UpdatedAt == expected.UpdatedAt
                    && s.SweptThrough == expected.SweptThrough
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(s => s.SweptThrough, sweptThrough)
                            .SetProperty(s => s.UpdatedAt, DateTime.UtcNow),
                    cancellationToken
                );
}
