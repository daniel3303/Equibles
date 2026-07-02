using Equibles.Data;
using Equibles.Media.BusinessLogic.Storage;
using Microsoft.EntityFrameworkCore;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.Media.HostedService;

/// <summary>
/// Reference checker over the financial context's File table — the context every
/// filesystem-stored blob is tracked in. Registered scoped by AddMediaWorker; the
/// sweep resolves all registered checkers so a multi-context deployment can add more.
/// </summary>
public class FinancialBlobReferenceChecker : IBlobReferenceChecker
{
    private readonly EquiblesFinancialDbContext _dbContext;

    public FinancialBlobReferenceChecker(EquiblesFinancialDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<bool> IsReferenced(string contentHash, CancellationToken cancellationToken)
    {
        return _dbContext
            .Set<File>()
            .AnyAsync(f => f.ContentHash == contentHash, cancellationToken);
    }

    public async Task<IReadOnlySet<string>> GetReferenced(
        IReadOnlyCollection<string> contentHashes,
        CancellationToken cancellationToken
    )
    {
        var referenced = await _dbContext
            .Set<File>()
            .Where(f => f.ContentHash != null && contentHashes.Contains(f.ContentHash))
            .Select(f => f.ContentHash)
            .Distinct()
            .ToListAsync(cancellationToken);
        return referenced.ToHashSet(StringComparer.Ordinal);
    }
}
