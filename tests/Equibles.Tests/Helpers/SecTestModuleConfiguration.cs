using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Tests.Helpers;

/// <summary>
/// Registers only Document and FailToDeliver entities for InMemory tests.
/// The production <see cref="Equibles.Sec.Data.SecModuleConfiguration"/> also
/// registers Chunk and Embedding, which use pgvector's <c>Vector</c> type that
/// the EF Core InMemory provider cannot construct. We explicitly ignore those
/// navigation properties so EF Core does not auto-discover them.
/// </summary>
public class SecTestModuleConfiguration : Equibles.Data.IModuleConfiguration {
    public void ConfigureEntities(ModelBuilder builder) {
        var docTypeConversion =
            new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DocumentType, string>(
                v => v.Value,
                v => DocumentType.FromValue(v) ?? new DocumentType(v));

        builder.Entity<Document>(b => {
            b.Property(e => e.DocumentType).HasConversion(docTypeConversion);
            b.Ignore(e => e.Chunks);
        });

        builder.Entity<FailToDeliver>();
        builder.Ignore<Chunk>();
        builder.Ignore<Embedding>();
    }
}
