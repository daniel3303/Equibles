using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.Data.Models;
using Equibles.Migrations.Migrations;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Microsoft.EntityFrameworkCore;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Sec;

// The BM25 date range trusts Chunk.ReportingDate, so the database keeps it equal to the parent's
// filing date for every writer.
[Collection(ParadeDbCollection.Name)]
public class ChunkReportingDateTriggerTests(ParadeDbFixture fixture) : ParadeDbMcpTestBase(fixture)
{
    [Fact]
    public async Task InsertedChunk_TakesItsDocumentDate_WhateverTheWriterPassed()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "INS");
        var document = AddDocument(issuer, new DateOnly(2026, 1, 15));
        var chunk = AddChunk(document, 0, new DateTime(2020, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        await DbContext.SaveChangesAsync();

        (await StoredDate(chunk.Id))
            .Should()
            .Be(new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task DocumentDateCorrection_MovesItsChunks_AndLeavesOtherDocumentsAlone()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "MOVE");
        var corrected = AddDocument(issuer, new DateOnly(2026, 9, 14));
        var first = AddChunk(corrected, 0);
        var second = AddChunk(corrected, 1);
        var untouched = AddChunk(AddDocument(issuer, new DateOnly(2026, 9, 14)), 0);
        await DbContext.SaveChangesAsync();

        corrected.ReportingDate = new DateOnly(2023, 4, 25);
        await DbContext.SaveChangesAsync();

        var correctedDate = new DateTime(2023, 4, 25, 0, 0, 0, DateTimeKind.Utc);
        (await StoredDate(first.Id)).Should().Be(correctedDate);
        (await StoredDate(second.Id)).Should().Be(correctedDate);
        (await StoredDate(untouched.Id))
            .Should()
            .Be(new DateTime(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task ChunkDateRewrite_IsOverriddenByItsDocumentDate()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "UPD");
        var chunk = AddChunk(AddDocument(issuer, new DateOnly(2026, 3, 2)), 0);
        await DbContext.SaveChangesAsync();

        chunk.ReportingDate = new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await DbContext.SaveChangesAsync();

        (await StoredDate(chunk.Id))
            .Should()
            .Be(new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task ChunkMovedToAnotherDocument_TakesTheNewParentsDate()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "MOV");
        var chunk = AddChunk(AddDocument(issuer, new DateOnly(2026, 3, 2)), 0);
        var target = AddDocument(issuer, new DateOnly(2024, 5, 6));
        await DbContext.SaveChangesAsync();

        chunk.Document = target;
        await DbContext.SaveChangesAsync();

        (await StoredDate(chunk.Id))
            .Should()
            .Be(new DateTime(2024, 5, 6, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Repair_RealignsChunksWrittenBeforeTheTriggers()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "FIX");
        var chunk = AddChunk(AddDocument(issuer, new DateOnly(2023, 4, 25)), 0);
        await DbContext.SaveChangesAsync();
        await DbContext.Database.ExecuteSqlRawAsync(
            """ALTER TABLE "Chunk" DISABLE TRIGGER "TR_Chunk_InheritReportingDate";"""
        );
        try
        {
            await DbContext.Database.ExecuteSqlRawAsync(
                """UPDATE "Chunk" SET "ReportingDate" = '2026-09-14T00:00:00Z' WHERE "Id" = {0};""",
                chunk.Id
            );
        }
        finally
        {
            await DbContext.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "Chunk" ENABLE TRIGGER "TR_Chunk_InheritReportingDate";"""
            );
        }
        (await StoredDate(chunk.Id))
            .Should()
            .Be(new DateTime(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc));

        await DbContext.Database.ExecuteSqlRawAsync(SyncChunkReportingDateWithDocument.RepairSql);

        (await StoredDate(chunk.Id))
            .Should()
            .Be(new DateTime(2023, 4, 25, 0, 0, 0, DateTimeKind.Utc));
    }

    private async Task<DateTime> StoredDate(Guid chunkId)
    {
        await using var context = Fixture.CreateDbContext();
        var stored = await context
            .Set<Chunk>()
            .AsNoTracking()
            .Where(chunk => chunk.Id == chunkId)
            .Select(chunk => chunk.ReportingDate)
            .SingleAsync();
        return stored.ToUniversalTime();
    }

    private Document AddDocument(EquityIssuer issuer, DateOnly date)
    {
        var document = new Document
        {
            Issuer = issuer,
            DocumentType = DocumentType.TenK,
            ReportingDate = date,
            Content = new File
            {
                Name = "filing",
                Extension = "txt",
                ContentType = "text/plain",
                Size = 1,
                FileContent = new FileContent { Bytes = [1] },
            },
        };
        DbContext.Add(document);
        return document;
    }

    private Chunk AddChunk(Document document, int index, DateTime? writerDate = null)
    {
        var chunk = new Chunk
        {
            Document = document,
            Index = index,
            Content = "orbital revenue",
            Ticker = document.Issuer.Presentation.Listing.Ticker,
            DocumentType = document.DocumentType,
            ReportingDate =
                writerDate
                ?? DateTime.SpecifyKind(
                    document.ReportingDate.ToDateTime(TimeOnly.MinValue),
                    DateTimeKind.Utc
                ),
        };
        DbContext.Add(chunk);
        return chunk;
    }
}
