using Equibles.Data;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Registers the financial-fact entities for in-memory tests, ignoring the
/// <see cref="FinancialFact.Document"/> navigation — it pulls in <c>Chunk</c> whose
/// <c>Pgvector.Vector</c> property the in-memory EF Core provider can't bind (mirrors
/// <see cref="DocumentOnlyModuleConfiguration"/>).
/// </summary>
internal sealed class FinancialFactsTestModuleConfiguration : IModuleConfiguration
{
    public void ConfigureEntities(ModelBuilder builder)
    {
        var conv = new ValueConverter<DocumentType, string>(
            v => v.Value,
            v => DocumentType.FromValue(v) ?? new DocumentType(v)
        );

        builder.Entity<FinancialConcept>();
        builder.Entity<FinancialFact>(b =>
        {
            b.Property(e => e.Form).HasConversion(conv);
            b.Ignore(e => e.Document);
        });
        builder.Entity<FinancialFactDimension>();
    }
}
