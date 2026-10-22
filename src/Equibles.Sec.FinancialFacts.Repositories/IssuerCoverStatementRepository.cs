using Equibles.Data;
using Equibles.Sec.FinancialFacts.Data.Models;
using FlexLabs.EntityFrameworkCore.Upsert;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.FinancialFacts.Repositories;

public class IssuerCoverStatementRepository : BaseRepository<IssuerCoverStatement>
{
    public IssuerCoverStatementRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    /// <summary>Statements filed within the inclusive date range, every issuer.</summary>
    public IQueryable<IssuerCoverStatement> GetFiledBetween(DateOnly from, DateOnly to) =>
        GetAll().Where(row => row.FiledDate >= from && row.FiledDate <= to);

    /// <summary>One row per filing: a re-read of the same filing replaces its statement.</summary>
    public Task Upsert(IssuerCoverStatement statement, CancellationToken cancellationToken) =>
        GetDbSet()
            .Upsert(statement)
            .On(row => row.DocumentId)
            .WhenMatched(
                (existing, incoming) =>
                    new IssuerCoverStatement
                    {
                        EquityIssuerId = incoming.EquityIssuerId,
                        AccessionNumber = incoming.AccessionNumber,
                        Form = incoming.Form,
                        FiledDate = incoming.FiledDate,
                        FileNumber = incoming.FileNumber,
                        IsShellCompany = incoming.IsShellCompany,
                    }
            )
            .RunAsync(cancellationToken);
}
