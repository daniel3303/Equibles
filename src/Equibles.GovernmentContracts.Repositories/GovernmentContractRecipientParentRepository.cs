using Equibles.Data;
using Equibles.GovernmentContracts.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.GovernmentContracts.Repositories;

public class GovernmentContractRecipientParentRepository
    : BaseRepository<GovernmentContractRecipientParent>
{
    public GovernmentContractRecipientParentRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public virtual async Task<List<GovernmentContractRecipientParent>> GetByRecipientIds(
        IReadOnlyCollection<string> recipientIds,
        CancellationToken cancellationToken = default
    )
    {
        return await GetAll()
            .Where(p => recipientIds.Contains(p.RecipientId))
            .ToListAsync(cancellationToken);
    }
}
