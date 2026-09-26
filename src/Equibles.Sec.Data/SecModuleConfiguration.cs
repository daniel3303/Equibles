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
            b.HasOne(row => row.Issuer)
                .WithMany()
                .HasForeignKey(row => row.EquityIssuerId)
                .OnDelete(DeleteBehavior.Restrict);
            b.Property(e => e.DocumentType).HasConversion(docTypeConversion);
            b.HasIndex(e => new { e.CreationTime, e.Id })
                .HasDatabaseName("IX_Document_PendingChunking")
                .HasFilter("\"ChunkedAt\" IS NULL")
                .IsCreatedConcurrently();
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

        builder.Entity<SecFilingArtifact>(b =>
        {
            b.Property(e => e.CaptureStatus).HasConversion<string>().HasMaxLength(32);
            b.HasOne(e => e.Document)
                .WithMany(d => d.Artifacts)
                .HasForeignKey(e => e.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<BackfillState>();
        builder
            .Entity<CompanyFilingSyncState>()
            .HasOne(state => state.Issuer)
            .WithMany()
            .HasForeignKey(state => state.EquityIssuerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .Entity<FailToDeliver>()
            .HasOne(row => row.Listing)
            .WithMany()
            .HasForeignKey(row => row.EquityListingId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<FailedFilingIngest>();
        builder.Entity<EsefOversizedReport>();
        builder.Entity<FormAdvAdviser>();
        builder
            .Entity<FormDFiling>()
            .HasOne(row => row.Issuer)
            .WithMany()
            .HasForeignKey(row => row.EquityIssuerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<FormDRelatedPerson>();
        builder.Entity<FundSeries>(b =>
        {
            b.HasOne(row => row.Issuer)
                .WithMany()
                .HasForeignKey(row => row.EquityIssuerId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(e => e.ClassTickers)
                .HasDatabaseName("IX_FundSeries_ClassTickers_Gin")
                .HasMethod("gin");
        });
        builder
            .Entity<NCenFiling>()
            .HasOne(row => row.Issuer)
            .WithMany()
            .HasForeignKey(row => row.EquityIssuerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<NCenServiceProvider>();
        builder
            .Entity<NportFiling>()
            .HasOne(row => row.Issuer)
            .WithMany()
            .HasForeignKey(row => row.EquityIssuerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<NportHolding>(b =>
        {
            b.HasIndex(h => h.Cusip)
                .HasDatabaseName("IX_NportHolding_CusipFiling")
                .IncludeProperties(h => h.NportFilingId)
                .IsCreatedConcurrently();
            // Foreign securities carry no CUSIP, so a fund's position in them is found by ISIN.
            b.HasIndex(h => h.Isin)
                .HasDatabaseName("IX_NportHolding_IsinFiling")
                .IncludeProperties(h => h.NportFilingId)
                .IsCreatedConcurrently();
            // Identifier co-statements count distinct filings across history; cover their
            // grouping and count without fetching every matching holding from the heap.
            b.HasIndex(h => new
                {
                    h.Isin,
                    h.Cusip,
                    h.Lei,
                    h.NportFilingId,
                })
                .HasDatabaseName("IX_NportHolding_EquityIdentity")
                .HasFilter("\"AssetCategory\" = 'EC' AND \"Isin\" IS NOT NULL")
                .IsCreatedConcurrently();
        });
        builder.Entity<ProcessedNportFiling>();
        builder
            .Entity<TranscriptCheckStatus>()
            .HasOne(state => state.Issuer)
            .WithMany()
            .HasForeignKey(state => state.EquityIssuerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
