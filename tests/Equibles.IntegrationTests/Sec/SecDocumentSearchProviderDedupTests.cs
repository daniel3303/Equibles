using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.Data.Models;
using Equibles.Search.Abstractions;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.Repositories;
using Equibles.Sec.Repositories.Search;
using Xunit;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Pins <see cref="SecDocumentSearchProvider.Search"/>'s documented contract
/// against real ParadeDB BM25: "collapses chunk hits to one entry per document".
/// Chunks are sub-document units; a query that matches several chunks of the
/// same filing must surface that filing once, not once per chunk — otherwise the
/// SEC Filings group is flooded with duplicate rows for one document. 0% covered.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class SecDocumentSearchProviderDedupTests : ParadeDbMcpTestBase
{
    public SecDocumentSearchProviderDedupTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Search_MultipleMatchingChunksPerDocument_CollapsesToOneHitPerDocument()
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

        // doc1 has THREE chunks all matching the rare token; doc2 has one. A
        // missing dedup would yield 4 hits instead of 2 (one per document).
        SeedChunk(doc1, 0, "zzqxquantum disclosure of segment revenue.", stock.Ticker);
        SeedChunk(doc1, 1, "Further zzqxquantum commentary on liquidity.", stock.Ticker);
        SeedChunk(doc1, 2, "Risk factors mention zzqxquantum exposure.", stock.Ticker);
        SeedChunk(doc2, 0, "Subsequent zzqxquantum events note.", stock.Ticker);

        await DbContext.SaveChangesAsync();

        var sut = new SecDocumentSearchProvider(new ChunkRepository(DbContext));

        var group = await sut.Search(
            new SearchRequest { Query = "zzqxquantum", MaxPerProvider = 5 },
            CancellationToken.None
        );

        group.Category.Should().Be("SEC Filings");
        group.Hits.Should().HaveCount(2, "four matching chunks span only two documents");
        group
            .Hits.Select(h => h.RouteValues["id"])
            .Should()
            .BeEquivalentTo([doc1.Id.ToString(), doc2.Id.ToString()]);
        group.Hits.Should().AllSatisfy(h => h.Kind.Should().Be("Filing"));
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

    private void SeedChunk(Document document, int index, string content, string ticker)
    {
        DbContext.Add(
            new Chunk
            {
                Document = document,
                DocumentId = document.Id,
                Index = index,
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
