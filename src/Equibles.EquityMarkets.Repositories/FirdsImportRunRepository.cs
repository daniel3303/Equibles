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

    // Each CFI category ('E', 'C') has its own full sets, named FULINS_{category}_*.
    public Task<DateOnly?> GetLatestFullPublication(
        string authority,
        char category,
        CancellationToken cancellationToken = default
    ) =>
        FullRuns(authority, category)
            .Select(row => (DateOnly?)row.PublishedOn)
            .MaxAsync(cancellationToken);

    public Task<bool> HasFullImport(
        string authority,
        char category = 'E',
        CancellationToken cancellationToken = default
    ) => FullRuns(authority, category).AnyAsync(cancellationToken);

    private IQueryable<FirdsImportRun> FullRuns(string authority, char category)
    {
        var prefix = "FULINS_" + category + "_";
        return GetAll()
            .Where(row =>
                row.Authority == authority
                && row.Kind == FirdsFileKind.Full
                && row.FileName.StartsWith(prefix)
            );
    }
}
