using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Media.Repositories;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Exercises <see cref="DocumentPersistenceService.ReplaceContent"/> against real Postgres: the
/// document's body file and line count are swapped in place — keeping the document id, so soft
/// references to it (e.g. an earnings call's TranscriptDocumentId) stay valid — and its existing
/// chunks are deleted so the chunking worker re-chunks the new body on its next pass.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class DocumentPersistenceServiceReplaceContentTests : ParadeDbMcpTestBase
{
    public DocumentPersistenceServiceReplaceContentTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private async Task<CommonStock> SeedCompany()
    {
        var apple = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        await using (var seed = Fixture.CreateDbContext())
        {
            seed.Set<CommonStock>().Add(apple);
            await seed.SaveChangesAsync();
        }
        DbContext.ChangeTracker.Clear();
        return await DbContext.Set<CommonStock>().SingleAsync(s => s.Id == apple.Id);
    }

    private DocumentPersistenceService BuildSut() =>
        new(
            new DocumentRepository(DbContext),
            new ChunkRepository(DbContext),
            new FileManager(new FileRepository(DbContext)),
            new DocumentImageService(
                new DocumentImageRepository(DbContext),
                new FileRepository(DbContext),
                new FileManager(new FileRepository(DbContext))
            ),
            Substitute.For<IBus>()
        );

    [Fact]
    public async Task ReplaceContent_SwapsBodyAndLineCount_AndDeletesStaleChunks()
    {
        var apple = await SeedCompany();

        // Persist an initial document, then add a chunk for it as the chunking worker would.
        await BuildSut()
            .Save(
                company: apple,
                content: "old line"u8.ToArray(),
                fileName: "AAPL-transcript.txt",
                documentType: DocumentType.TenK,
                reportingDate: new DateOnly(2024, 3, 15),
                reportingForDate: new DateOnly(2023, 12, 31),
                sourceUrl: "https://example.test/filing"
            );

        Guid documentId;
        await using (var seed = Fixture.CreateDbContext())
        {
            var document = await seed.Set<Document>().SingleAsync(d => d.CommonStockId == apple.Id);
            documentId = document.Id;
            seed.Set<Chunk>()
                .Add(
                    new Chunk
                    {
                        DocumentId = document.Id,
                        Index = 0,
                        StartPosition = 0,
                        EndPosition = 8,
                        StartLineNumber = 1,
                        Content = "old line",
                        DocumentType = DocumentType.TenK,
                        Ticker = apple.Ticker,
                        ReportingDate = new DateTime(2024, 3, 15, 0, 0, 0, DateTimeKind.Utc),
                    }
                );
            await seed.SaveChangesAsync();
        }

        DbContext.ChangeTracker.Clear();
        var tracked = await new DocumentRepository(DbContext).Get(documentId);
        var newBody = "new line 1\nnew line 2\nnew line 3"u8.ToArray();

        await BuildSut().ReplaceContent(tracked, newBody);

        await using var verify = Fixture.CreateDbContext();
        var saved = await verify.Set<Document>().SingleAsync(d => d.Id == documentId);
        saved.LineCount.Should().Be(3);

        var bodyFile = await verify.Set<File>().SingleAsync(f => f.Id == saved.ContentId);
        bodyFile.FileContent.Bytes.Should().Equal(newBody);

        var remainingChunks = await verify.Set<Chunk>().CountAsync(c => c.DocumentId == documentId);
        remainingChunks.Should().Be(0);
    }
}
