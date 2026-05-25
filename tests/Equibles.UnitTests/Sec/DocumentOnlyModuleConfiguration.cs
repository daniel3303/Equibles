using Equibles.Data;
using Equibles.Sec.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Registers only <see cref="Document"/> for in-memory tests — excludes
/// <c>Chunk</c> whose <c>Pgvector.Vector</c> property is incompatible
/// with the in-memory EF Core provider.
/// </summary>
internal sealed class DocumentOnlyModuleConfiguration : IModuleConfiguration
{
    public void ConfigureEntities(ModelBuilder builder)
    {
        var conv = new ValueConverter<DocumentType, string>(
            v => v.Value,
            v => DocumentType.FromValue(v) ?? new DocumentType(v)
        );

        builder.Entity<Document>(b =>
        {
            b.Property(e => e.DocumentType).HasConversion(conv);
            b.Ignore(e => e.Chunks);
        });
    }
}
