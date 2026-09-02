using Equibles.Data;
using Equibles.Sec.Data.Models;

namespace Equibles.Sec.Repositories;

public class SecFilingArtifactRepository : BaseRepository<SecFilingArtifact>
{
    public SecFilingArtifactRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<SecFilingArtifact> GetByDocument(Document document)
    {
        return Order(GetAll().Where(artifact => artifact.DocumentId == document.Id));
    }

    public static IOrderedQueryable<SecFilingArtifact> Order(
        IQueryable<SecFilingArtifact> artifacts
    ) =>
        artifacts
            .OrderBy(artifact => artifact.SequenceNumber == null)
            .ThenBy(artifact => artifact.SequenceNumber)
            .ThenBy(artifact => artifact.Sequence)
            .ThenBy(artifact => artifact.FileName);
}
