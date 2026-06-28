using Equibles.Data;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Registers <see cref="Document"/> and <see cref="Chunk"/> for in-memory tests —
/// the filing body is stored as chunks, so a test exercising chunk-backed reads
/// needs both. <c>Chunk.Embeddings</c> is ignored because the <c>Embedding</c>
/// entity carries a <c>Pgvector.Vector</c> the in-memory provider cannot map.
/// </summary>
internal sealed class DocumentAndChunkModuleConfiguration : IModuleConfiguration
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

        builder.Entity<Chunk>(b =>
        {
            b.Property(e => e.DocumentType).HasConversion(conv);
            b.Ignore(e => e.Embeddings);
            b.Ignore(e => e.Document);
        });
    }
}
