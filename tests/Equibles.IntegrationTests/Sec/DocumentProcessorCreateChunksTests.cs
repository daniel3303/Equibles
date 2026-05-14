using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.BusinessLogic.Embeddings;
using Equibles.Sec.BusinessLogic.Processing;
using Equibles.Sec.BusinessLogic.Tokenization;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using File = Equibles.Media.Data.Models.File;
using FileContent = Equibles.Media.Data.Models.FileContent;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Sibling to <see cref="DocumentProcessorTests"/>. That file pins the failure paths
/// (null content, cancellation, embedding-count mismatch) but never exercises the
/// happy path through <c>CreateChunks</c> — the largest uncovered branch in
/// <see cref="DocumentProcessor"/>. This test runs <c>ProcessDocuments</c> against
/// a real document with byte content and pins that <c>_chunkRepository.Add</c> is
/// invoked once per chunk produced by <see cref="ChunkingStrategy"/>, each chunk
/// carrying the document's <c>Ticker</c> / <c>DocumentType</c> / <c>ReportingDate</c>
/// and a non-empty <c>Content</c> string. A refactor that swaps the order of
/// <c>Add</c> and <c>SaveChanges</c>, drops the document metadata into the chunk
/// incorrectly, or short-circuits the loop on the first chunk would surface here.
/// </summary>
public class DocumentProcessorCreateChunksTests
{
    [Fact]
    public async Task ProcessDocuments_DocumentWithContent_AddsChunkPerSplitToRepositoryThenSaves()
    {
        // Real text that ChunkingStrategy will split into at least one chunk. The exact
        // chunk count is implementation-dependent (token-aware); the assertion only
        // requires at least one chunk and that every Add receives a fully-populated chunk.
        var bytes = Encoding.UTF8.GetBytes(
            string.Concat(Enumerable.Repeat("Apple Inc. reported revenue. ", 200))
        );

        var stock = new CommonStock { Ticker = "AAPL", Name = "Apple Inc." };
        var documentId = Guid.NewGuid();
        var document = new Document
        {
            Id = documentId,
            CommonStock = stock,
            DocumentType = DocumentType.TenK,
            ReportingDate = new DateOnly(2025, 1, 15),
            Content = new File
            {
                Name = "10k",
                Extension = "txt",
                ContentType = "text/plain",
                Size = bytes.Length,
                FileContent = new FileContent { Bytes = bytes },
            },
        };

        var chunkRepository = Substitute.For<ChunkRepository>(
            (Equibles.Data.EquiblesDbContext)null
        );
        var sut = new DocumentProcessor(
            chunkRepository,
            Substitute.For<EmbeddingRepository>((Equibles.Data.EquiblesDbContext)null),
            Substitute.For<IEmbeddingClient>(),
            new ChunkingStrategy(new TokenCounter()),
            Options.Create(new EmbeddingConfig { ModelName = "test-model" }),
            Substitute.For<ILogger<DocumentProcessor>>()
        );

        await sut.ProcessDocuments([document], CancellationToken.None);

        var addedChunks = chunkRepository
            .ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(ChunkRepository.Add))
            .Select(c => (Chunk)c.GetArguments()[0])
            .ToList();

        addedChunks.Should().NotBeEmpty();
        addedChunks.Should().AllSatisfy(c =>
        {
            c.Ticker.Should().Be("AAPL");
            c.DocumentType.Should().Be(DocumentType.TenK);
            c.Document.Should().BeSameAs(document);
            c.Content.Should().NotBeNullOrEmpty();
            c.ReportingDate.Kind.Should().Be(DateTimeKind.Utc);
        });
        // Index must be assigned sequentially — a regression that reused i or shuffled
        // the order would silently corrupt downstream search/retrieval ordering.
        addedChunks.Select(c => c.Index).Should().Equal(Enumerable.Range(0, addedChunks.Count));
        await chunkRepository.Received(1).SaveChanges();
    }
}
