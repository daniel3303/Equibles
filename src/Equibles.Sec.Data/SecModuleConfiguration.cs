using Equibles.Sec.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Data;

public class SecModuleConfiguration : Equibles.Data.IFinancialModule
{
    public void ConfigureEntities(ModelBuilder builder)
    {
        var docTypeConversion =
            new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<
                DocumentType,
                string
            >(v => v.Value, v => DocumentType.FromValue(v) ?? new DocumentType(v));

        builder.Entity<Document>(b =>
        {
            b.Property(e => e.DocumentType).HasConversion(docTypeConversion);
        });

        builder.Entity<Models.Chunks.Chunk>(b =>
        {
            b.Property(e => e.DocumentType).HasConversion(docTypeConversion);
        });

        builder.Entity<DocumentImage>(b =>
        {
            // The link rows cascade with the document (the doc owns its image set). The File blob
            // is restrict — it's cleaned up on the same app delete path as the document's other
            // artifacts, after the link row is gone — so File can't be deleted while still referenced.
            b.HasOne(e => e.Document)
                .WithMany(d => d.Images)
                .HasForeignKey(e => e.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(e => e.File)
                .WithMany()
                .HasForeignKey(e => e.FileId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<FailToDeliver>();
        builder.Entity<FormAdvAdviser>();
        builder.Entity<FormDFiling>();
        builder.Entity<FormDRelatedPerson>();
        builder.Entity<FundSeries>();
        builder.Entity<NCenFiling>();
        builder.Entity<NCenServiceProvider>();
        builder.Entity<NportFiling>();
        builder.Entity<NportHolding>();
        builder.Entity<ProcessedNportFiling>();
        builder.Entity<TranscriptCheckStatus>();
    }
}
