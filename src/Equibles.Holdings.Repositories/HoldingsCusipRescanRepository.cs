using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Repositories;

public class HoldingsCusipRescanRepository(EquiblesFinancialDbContext dbContext)
    : BaseRepository<HoldingsCusipRescan>(dbContext)
{
    public Task<int> Enqueue(HoldingsCusipRescan request, CancellationToken cancellationToken) =>
        DbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "HoldingsCusipRescan"
                ("Id", "EquityIssuerId", "Ticker", "PreviousCusip", "Cusip", "RequestedAt")
            VALUES ({request.Id}, {request.EquityIssuerId}, {request.Ticker},
                {request.PreviousCusip}, {request.Cusip}, {request.RequestedAt})
            ON CONFLICT ("Id") DO NOTHING
            """,
            cancellationToken
        );
}
