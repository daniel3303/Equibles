using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Repositories;

public class Form13FConfidentialFilingRepository(EquiblesFinancialDbContext dbContext)
    : BaseRepository<Form13FConfidentialFiling>(dbContext)
{
    // The realtime sweep and the archive reader can meet the same accession; the first one wins.
    public Task Record(
        string accession,
        string filerCik,
        DateOnly reportDate,
        DateOnly filingDate,
        bool isAmendment,
        int? declaredEntryTotal,
        int disclosedPositionCount,
        CancellationToken cancellationToken
    ) =>
        DbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "Form13FConfidentialFiling"
              ("Id", "AccessionNumber", "FilerCik", "ReportDate", "FilingDate", "IsAmendment",
               "DeclaredEntryTotal", "DisclosedPositionCount")
            VALUES ({Guid.NewGuid()}, {accession}, {filerCik}, {reportDate}, {filingDate}, {isAmendment},
                    {declaredEntryTotal}, {disclosedPositionCount})
            ON CONFLICT ("AccessionNumber") DO NOTHING
            """,
            cancellationToken
        );
}
