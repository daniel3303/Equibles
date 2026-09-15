using Equibles.Data;
using Equibles.EquityMarkets.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.EquityMarkets.Repositories;

public class FirdsImportRunRepository(EquiblesFinancialDbContext dbContext)
    : BaseRepository<FirdsImportRun>(dbContext)
{
    public Task<bool> Exists(
        string authority,
        string fileName,
        CancellationToken cancellationToken = default
    ) =>
        GetAll()
            .AnyAsync(
                row => row.Authority == authority && row.FileName == fileName,
                cancellationToken
            );

    public Task<DateOnly?> GetLatestFullPublication(
        string authority,
        CancellationToken cancellationToken = default
    ) =>
        GetAll()
            .Where(row => row.Authority == authority && row.Kind == FirdsFileKind.Full)
            .Select(row => (DateOnly?)row.PublishedOn)
            .MaxAsync(cancellationToken);

    public Task<bool> HasFullImport(
        string authority,
        CancellationToken cancellationToken = default
    ) =>
        GetAll()
            .AnyAsync(
                row => row.Authority == authority && row.Kind == FirdsFileKind.Full,
                cancellationToken
            );
}
