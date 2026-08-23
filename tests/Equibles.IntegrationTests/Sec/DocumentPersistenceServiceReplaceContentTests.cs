using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Media.BusinessLogic.Configuration;
using Equibles.Media.Data.Models;
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
public class DocumentPersistenceServiceReplaceContentTests : ParadeDbMcpTestBase, IDisposable
{
    private readonly string _storageRoot = Path.Combine(
        Path.GetTempPath(),
        $"equibles-replace-content-{Guid.NewGuid():N}"
    );

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

    private DocumentPersistenceService BuildSut()
    {
        var storageOptions = new FileStorageOptions { Enabled = true, RootPath = _storageRoot };
        var pendingDeletions = new PendingBlobDeletionRepository(DbContext);
        var fileManager = FileManagerTestFactory.Create(
            new FileRepository(DbContext),
            storageOptions,
            pendingDeletions
        );

        return new(
            new DocumentRepository(DbContext),
            new ChunkRepository(DbContext),
            fileManager,
            new DocumentImageService(
                new DocumentImageRepository(DbContext),
                new FileRepository(DbContext),
                fileManager
            ),
            Substitute.For<IBus>()
        );
    }

    public void Dispose()
    {
        if (Directory.Exists(_storageRoot))
            Directory.Delete(_storageRoot, recursive: true);
    }

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
        Guid oldContentId;
        string oldContentHash;
        string oldRelativePath;
        await using (var seed = Fixture.CreateDbContext())
        {
            var document = await seed.Set<Document>()
                .Include(d => d.Content)
                .SingleAsync(d => d.CommonStockId == apple.Id);
            documentId = document.Id;
            oldContentId = document.ContentId;
            oldContentHash = document.Content.ContentHash;
            oldRelativePath = document.Content.RelativePath;
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
        await using (var concurrent = Fixture.CreateDbContext())
        {
            await concurrent
                .Set<Document>()
                .Where(d => d.Id == documentId)
                .ExecuteUpdateAsync(setters =>
                    setters
                        .SetProperty(d => d.Items, "7.01")
                        .SetProperty(d => d.ChunkedAt, DateTime.UtcNow)
                );
        }
        tracked.ChunkedAt.Should().BeNull("the replacement context loaded the legacy marker");
        var newBody = "new line 1\nnew line 2\nnew line 3"u8.ToArray();

        await BuildSut().ReplaceContent(tracked, newBody);

        await using var verify = Fixture.CreateDbContext();
        var saved = await verify.Set<Document>().SingleAsync(d => d.Id == documentId);
        saved.LineCount.Should().Be(3);
        saved.Items.Should().Be("7.01");
        saved.NormalizedContentVersion.Should().Be(Document.NormalizedContentBuilderVersion);
        saved.ChunkedAt.Should().BeNull();

        var bodyFile = await verify.Set<File>().SingleAsync(f => f.Id == saved.ContentId);
        bodyFile.StorageProvider.Should().Be(StorageProvider.FileSystem);
        var bodyBytes = await System.IO.File.ReadAllBytesAsync(
            Path.Combine(_storageRoot, bodyFile.RelativePath)
        );
        bodyBytes.Should().Equal(newBody);

        (await verify.Set<File>().AnyAsync(f => f.Id == oldContentId)).Should().BeFalse();
        var queuedDeletion = await verify.Set<PendingBlobDeletion>().SingleAsync();
        queuedDeletion.ContentHash.Should().Be(oldContentHash);
        queuedDeletion.RelativePath.Should().Be(oldRelativePath);

        var remainingChunks = await verify.Set<Chunk>().CountAsync(c => c.DocumentId == documentId);
        remainingChunks.Should().Be(0);
    }

    [Fact]
    public async Task ResetChunks_ClearsChunkedMarkerAndDeletesChunks()
    {
        var apple = await SeedCompany();
        await BuildSut()
            .Save(
                company: apple,
                content: "stored body"u8.ToArray(),
                fileName: "AAPL-10k.txt",
                documentType: DocumentType.TenK,
                reportingDate: new DateOnly(2024, 3, 15),
                reportingForDate: new DateOnly(2023, 12, 31),
                sourceUrl: "https://example.test/filing"
            );

        var document = await DbContext
            .Set<Document>()
            .SingleAsync(d => d.CommonStockId == apple.Id);
        DbContext
            .Set<Chunk>()
            .Add(
                new Chunk
                {
                    DocumentId = document.Id,
                    Index = 0,
                    Content = "stored body",
                    DocumentType = document.DocumentType,
                    Ticker = apple.Ticker,
                    ReportingDate = document.ReportingDate.ToDateTime(
                        TimeOnly.MinValue,
                        DateTimeKind.Utc
                    ),
                }
            );
        await DbContext.SaveChangesAsync();

        await using (var concurrent = Fixture.CreateDbContext())
        {
            await concurrent
                .Set<Document>()
                .Where(d => d.Id == document.Id)
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(d => d.ChunkedAt, DateTime.UtcNow)
                );
        }
        document.ChunkedAt.Should().BeNull("the reset context loaded the legacy marker");

        await BuildSut().ResetChunks(document);

        await using var verify = Fixture.CreateDbContext();
        var saved = await verify.Set<Document>().SingleAsync(d => d.Id == document.Id);
        saved.ChunkedAt.Should().BeNull();
        (await verify.Set<Chunk>().AnyAsync(c => c.DocumentId == document.Id)).Should().BeFalse();
    }
}
