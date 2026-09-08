using Equibles.Data;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Registers financial facts and their source documents without the vector-bearing chunks.
/// </summary>
internal sealed class FinancialFactsTestModuleConfiguration : IModuleConfiguration
{
    public void ConfigureEntities(ModelBuilder builder)
    {
        var conv = new ValueConverter<DocumentType, string>(
            v => v.Value,
            v => DocumentType.FromValue(v) ?? new DocumentType(v)
        );

        new Equibles.Media.Data.MediaModuleConfiguration().ConfigureEntities(builder);
        new DocumentOnlyModuleConfiguration().ConfigureEntities(builder);
        builder.Entity<FinancialConcept>();
        builder.Entity<FinancialFact>(b =>
        {
            b.Property(e => e.Form).HasConversion(conv);
        });
        builder.Entity<FinancialFactDimension>();
    }
}
