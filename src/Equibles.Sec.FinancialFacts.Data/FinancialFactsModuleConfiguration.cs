using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Equibles.Sec.FinancialFacts.Data;

public class FinancialFactsModuleConfiguration : Equibles.Data.IModuleConfiguration
{
    public void ConfigureEntities(ModelBuilder builder)
    {
        // Same DocumentType <-> string conversion the SEC module uses, so
        // FinancialFact.Form persists identically to Document.DocumentType.
        var docTypeConversion = new ValueConverter<DocumentType, string>(
            v => v.Value,
            v => DocumentType.FromValue(v) ?? new DocumentType(v)
        );

        builder.Entity<FinancialConcept>();

        builder.Entity<FinancialFact>(b =>
        {
            b.Property(e => e.Form).HasConversion(docTypeConversion);
        });

        builder.Entity<FinancialFactsSyncStatus>();
    }
}
