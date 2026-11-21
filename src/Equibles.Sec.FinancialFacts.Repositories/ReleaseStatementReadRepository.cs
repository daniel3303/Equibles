using Equibles.Data;
using Equibles.Sec.FinancialFacts.Data.Models;

namespace Equibles.Sec.FinancialFacts.Repositories;

public class ReleaseStatementReadRepository : BaseRepository<ReleaseStatementRead>
{
    public ReleaseStatementReadRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<ReleaseStatementRead> GetByDocumentId(Guid documentId) =>
        GetAll().Where(read => read.DocumentId == documentId);

    /// <summary>Releases already read under the current matching rule.</summary>
    public IQueryable<ReleaseStatementRead> GetCurrent() =>
        GetAll().Where(read => read.MatcherVersion >= ReleaseStatementRead.CurrentMatcherVersion);
}
