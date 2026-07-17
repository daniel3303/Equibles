using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Pins <see cref="Equibles.Sec.BusinessLogic.Search.HybridChunkSearcher"/>'s opt-in
/// disjunctive fallback over the real BM25 ranking. The conjunctive default ANDs every
/// query token, so a wordy natural-language query with one non-matching token ("drivers"
/// vs the filing's "driven") returns nothing; with the fallback enabled the searcher
/// re-runs the pass in any-token mode and tops the result up. The default path must be
/// byte-for-byte unchanged — ALVIS and the portal RAG share this searcher.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HybridChunkSearcherDisjunctiveFallbackTests : ParadeDbMcpTestBase
{
    public HybridChunkSearcherDisjunctiveFallbackTests(ParadeDbFixture fixture)
        : base(fixture) { }

    // One query token ("drivers") appears nowhere in the chunk, so the conjunctive pass
    // matches nothing even though the chunk is on-point for the remaining tokens.
    private const string WordyQuery = "data center revenue growth drivers";
    private const string OnPointContent =
        "Revenue from the Data Center segment was strong this quarter.";

    [Fact]
    public async Task Search_DefaultConjunctive_OneNonMatchingTokenReturnsNothing()
    {
        await SeedOnPointChunk();
        var sut = HybridChunkSearcherFactory.Bm25Only(DbContext);

        var results = await sut.Search(WordyQuery, maxResults: 5);

        // Pins the premise the fallback test rests on AND the unchanged default for the
        // shared consumers: without opting in, conjunctive semantics still apply.
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task Search_DisjunctiveFallback_RecoversChunksTheConjunctivePassStarved()
    {
        var document = await SeedOnPointChunk();
        var sut = HybridChunkSearcherFactory.Bm25Only(DbContext);

        var results = await sut.Search(
            WordyQuery,
            maxResults: 5,
            documentId: document.Id,
            disjunctiveFallback: true
        );

        results.Should().NotBeEmpty();
        results.Should().AllSatisfy(c => c.DocumentId.Should().Be(document.Id));
    }

    [Fact]
    public async Task Search_DisjunctiveFallback_ConjunctiveHitsKeepTheirRankAheadOfBroadHits()
    {
        var stock = SeedStock();
        var document = SeedDocument(stock);
        // Chunk 0 matches every token; chunk 1 only some — the fallback must append the
        // broad hit AFTER the precise one, never displace it.
        SeedChunk(
            document,
            "Data center revenue growth drivers include accelerated computing.",
            stock.Ticker,
            0
        );
        SeedChunk(document, OnPointContent, stock.Ticker, 1);
        await DbContext.SaveChangesAsync();

        var sut = HybridChunkSearcherFactory.Bm25Only(DbContext);

        var results = await sut.Search(
            WordyQuery,
            maxResults: 5,
            documentId: document.Id,
            disjunctiveFallback: true
        );

        results.Should().HaveCount(2);
        results[0].Index.Should().Be(0);
        results[1].Index.Should().Be(1);
    }

    private async Task<Document> SeedOnPointChunk()
    {
        var stock = SeedStock();
        var document = SeedDocument(stock);
        SeedChunk(document, OnPointContent, stock.Ticker);
        await DbContext.SaveChangesAsync();
        return document;
    }

    private CommonStock SeedStock()
    {
        var stock = new CommonStock
        {
            Ticker = "NVDA",
            Name = "Nvidia Corp.",
            Cik = Random.Shared.NextInt64(1_000_000_000L, 9_999_999_999L).ToString(),
        };
        DbContext.Add(stock);
        return stock;
    }

    private Document SeedDocument(CommonStock stock)
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
            ReportingDate = new DateOnly(2026, 2, 25),
            ReportingForDate = new DateOnly(2026, 1, 25),
            LineCount = 1,
        };
        DbContext.Add(document);
        return document;
    }

    private void SeedChunk(Document document, string content, string ticker, int index = 0)
    {
        DbContext.Add(
            new Chunk
            {
                Document = document,
                DocumentId = document.Id,
                Index = index,
                StartPosition = index * 100,
                EndPosition = index * 100 + content.Length,
                StartLineNumber = index + 1,
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
