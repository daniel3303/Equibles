using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.Repositories;
using Xunit;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Pins <see cref="ChunkRepository.HybridSearch"/>'s <c>documentId</c> filter
/// against real ParadeDB BM25. Every other HybridSearch filter (ticker,
/// documentType, date range) is pinned indirectly via <c>RagSearchToolsTests</c>,
/// but documentId is only reached through <c>RagManager.SearchRelevantChunksByDocument</c>
/// — and that method has zero callers in tests. A refactor that simplified
/// HybridSearch's filter chain (e.g., consolidating predicates into a
/// generated builder) and dropped <c>q => q.Where(c => c.DocumentId == documentId.Value)</c>
/// would silently let chunks from sibling documents leak into per-document
/// search results, corrupting any "ask a question about THIS filing" UI.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class ChunkRepositoryHybridSearchDocumentIdFilterTests : ParadeDbMcpTestBase
{
    public ChunkRepositoryHybridSearchDocumentIdFilterTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task HybridSearch_WithDocumentIdFilter_ReturnsOnlyChunksFromThatDocument()
    {
        var stock = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        DbContext.Add(stock);

        var doc1 = SeedDocument(stock, new DateOnly(2026, 1, 15));
        var doc2 = SeedDocument(stock, new DateOnly(2026, 4, 15));

        // Both documents contain a chunk that matches the BM25 query. Without the
        // documentId predicate, both would rank into the result set.
        SeedChunk(doc1, "Services revenue grew substantially this quarter.", stock.Ticker);
        SeedChunk(doc2, "Services revenue continued its strong growth trajectory.", stock.Ticker);

        await DbContext.SaveChangesAsync();

        var sut = new ChunkRepository(DbContext);

        var results = await sut.HybridSearch(
            searchText: "services revenue",
            maxResults: 10,
            documentId: doc1.Id
        );

        results.Should().NotBeEmpty();
        results.Should().AllSatisfy(c => c.DocumentId.Should().Be(doc1.Id));
    }

    private Document SeedDocument(CommonStock stock, DateOnly reportingDate)
    {
        var fileContent = new FileContent { Bytes = "placeholder"u8.ToArray() };
        var file = new File
        {
            Name = "filing",
            Extension = "txt",
            ContentType = "text/plain",
            Size = fileContent.Bytes.Length,
            FileContent = fileContent,
        };
        fileContent.FileId = file.Id;
        DbContext.Add(file);

        var document = new Document
        {
            CommonStock = stock,
            CommonStockId = stock.Id,
            Content = file,
            ContentId = file.Id,
            DocumentType = DocumentType.TenK,
            ReportingDate = reportingDate,
            ReportingForDate = reportingDate.AddDays(-30),
            LineCount = 1,
        };
        DbContext.Add(document);
        return document;
    }

    private void SeedChunk(Document document, string content, string ticker)
    {
        DbContext.Add(
            new Chunk
            {
                Document = document,
                DocumentId = document.Id,
                Index = 0,
                StartPosition = 0,
                EndPosition = content.Length,
                StartLineNumber = 1,
                Content = content,
                DocumentType = document.DocumentType,
                Ticker = ticker,
                ReportingDate = DateTime.SpecifyKind(
                    document.ReportingDate.ToDateTime(TimeOnly.MinValue),
                    DateTimeKind.Utc
                ),
            }
        );
    }
}
