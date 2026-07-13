using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Pins <see cref="Equibles.Sec.BusinessLogic.Search.HybridChunkSearcher"/>'s
/// per-company cap over the real BM25 ranking: one chatty filer must not fill the
/// whole result set. The searcher over-fetches the BM25 pool and keeps each company's
/// best-ranked chunks up to the cap, so the freed slots refill with the next-best
/// companies instead of shrinking the result.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HybridChunkSearcherPerCompanyCapTests : ParadeDbMcpTestBase
{
    public HybridChunkSearcherPerCompanyCapTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Search_PerCompanyCap_SpreadsResultsAcrossCompanies()
    {
        // Apple matches six times over — without the cap it fills all four slots.
        var apple = SeedStock("AAPL", "Apple Inc.", "0000320193");
        var appleFiling = SeedDocument(apple);
        for (var index = 0; index < 6; index++)
            SeedChunk(
                appleFiling,
                $"Services revenue grew substantially in segment {index}.",
                "AAPL",
                index
            );
        var microsoft = SeedStock("MSFT", "Microsoft Corp.", "0000789019");
        SeedChunk(SeedDocument(microsoft), "Services revenue rose across the cloud unit.", "MSFT");
        await DbContext.SaveChangesAsync();

        var sut = HybridChunkSearcherFactory.Bm25Only(DbContext);

        var results = await sut.Search("services revenue", maxResults: 4, maxResultsPerCompany: 2);

        results.Should().NotBeEmpty();
        results.Count(c => c.Ticker == "AAPL").Should().BeLessThanOrEqualTo(2);
        results.Select(c => c.Ticker).Should().Contain("MSFT");
    }

    [Fact]
    public async Task Search_NoCap_KeepsThePlainRelevanceOrdering()
    {
        var apple = SeedStock("AAPL", "Apple Inc.", "0000320193");
        var appleFiling = SeedDocument(apple);
        for (var index = 0; index < 3; index++)
            SeedChunk(
                appleFiling,
                $"Services revenue grew substantially in segment {index}.",
                "AAPL",
                index
            );
        await DbContext.SaveChangesAsync();

        var sut = HybridChunkSearcherFactory.Bm25Only(DbContext);

        var results = await sut.Search("services revenue", maxResults: 3);

        results.Should().HaveCount(3);
        results.Should().AllSatisfy(c => c.Ticker.Should().Be("AAPL"));
    }

    private CommonStock SeedStock(string ticker, string name, string cik)
    {
        var stock = new CommonStock
        {
            Ticker = ticker,
            Name = name,
            Cik = cik,
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
            ReportingDate = new DateOnly(2026, 1, 15),
            ReportingForDate = new DateOnly(2025, 12, 31),
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
