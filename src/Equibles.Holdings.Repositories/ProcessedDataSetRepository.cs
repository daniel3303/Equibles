using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Repositories;

public class ProcessedDataSetRepository : BaseRepository<ProcessedDataSet>
{
    public ProcessedDataSetRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public virtual Task QueueRealtimeReplay(CancellationToken cancellationToken) =>
        DbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "ProcessedDataSet" ("Id", "FileName", "SubmissionCount", "ParserVersion", "CreationTime")
            VALUES ({Guid.NewGuid()}, {ProcessedDataSet.RealtimeReplayPendingFileName}, 0, 0, clock_timestamp())
            ON CONFLICT ("FileName") DO NOTHING
            """,
            cancellationToken
        );

    public IQueryable<ProcessedDataSet> GetByFileName(string fileName)
    {
        return GetAll().Where(p => p.FileName == fileName);
    }
}
