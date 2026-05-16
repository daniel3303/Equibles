using System.Reflection;
using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.BusinessLogic.Embeddings;
using Equibles.Sec.BusinessLogic.Processing;
using Equibles.Sec.BusinessLogic.Tokenization;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Pins <c>DocumentProcessor.ChunkDocument</c>'s three early-return guards
/// (null document, already-chunked, empty content) — the happy chunking path is
/// covered elsewhere, these defensive skips were not. Built against the
/// ParadeDB context (the Sec module's pgvector embedding entity can't be
/// modelled by the EF in-memory provider) and driven directly via reflection;
/// the guards return before any repository is touched.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class DocumentProcessorChunkDocumentGuardTests : ParadeDbMcpTestBase
{
    public DocumentProcessorChunkDocumentGuardTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private DocumentProcessor BuildProcessor() =>
        new(
            new ChunkRepository(DbContext),
            new EmbeddingRepository(DbContext),
            Substitute.For<IEmbeddingClient>(),
            new ChunkingStrategy(new TokenCounter()),
            Options.Create(new EmbeddingConfig()),
            Substitute.For<ILogger<DocumentProcessor>>()
        );

    private static Task InvokeChunk(DocumentProcessor sut, Document document)
    {
        var m = typeof(DocumentProcessor).GetMethod(
            "ChunkDocument",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        return (Task)m.Invoke(sut, [document]);
    }

    [Fact]
    public async Task ChunkDocument_NullDocument_LogsAndReturns()
    {
        var act = async () => await InvokeChunk(BuildProcessor(), null);

        await act.Should().NotThrowAsync("a null document must be skipped, not crash");
    }

    [Fact]
    public async Task ChunkDocument_AlreadyChunked_SkipsWithoutReprocessing()
    {
        var document = new Document
        {
            DocumentType = DocumentType.TenK,
            Chunks = [new Chunk { Index = 0 }],
        };

        var act = async () => await InvokeChunk(BuildProcessor(), document);

        await act.Should().NotThrowAsync("an already-chunked document is skipped");
    }

    [Fact]
    public async Task ChunkDocument_WhitespaceContent_LogsNoContentAndReturns()
    {
        var document = new Document
        {
            DocumentType = DocumentType.TenK,
            CommonStock = new CommonStock { Ticker = "AAPL", Name = "Apple Inc." },
            Content = new File
            {
                FileContent = new Equibles.Media.Data.Models.FileContent
                {
                    Bytes = Encoding.UTF8.GetBytes("   \n\t  "),
                },
            },
        };

        var act = async () => await InvokeChunk(BuildProcessor(), document);

        await act.Should().NotThrowAsync("whitespace-only content is treated as no content");
    }
}
