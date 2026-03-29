using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.BusinessLogic.Search;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.CommonStocks.Repositories;
using Equibles.Sec.Repositories;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.Tests.Sec;

public class RagManagerTests {
    private static Chunk MakeChunk(
        string ticker = "AAPL", string companyName = "Apple Inc",
        string content = "Sample content", int index = 0,
        int startPosition = 0, int startLineNumber = 0,
        DateOnly? reportingDate = null, DocumentType documentType = null
    ) {
        var stock = new CommonStock { Ticker = ticker, Name = companyName };
        var doc = new Document {
            CommonStock = stock,
            CommonStockId = stock.Id,
            DocumentType = documentType ?? DocumentType.TenK,
            ReportingDate = reportingDate ?? new DateOnly(2024, 3, 15),
        };
        return new Chunk {
            Document = doc,
            DocumentId = doc.Id,
            Content = content,
            Index = index,
            StartPosition = startPosition,
            StartLineNumber = startLineNumber,
            DocumentType = doc.DocumentType,
            Ticker = ticker,
        };
    }

    private static RagManager CreateSut() {
        return new RagManager(
            Substitute.For<ChunkRepository>((Equibles.Data.EquiblesDbContext)null),
            Substitute.For<CommonStockRepository>((Equibles.Data.EquiblesDbContext)null),
            Substitute.For<ILogger<RagManager>>());
    }

    // ── BuildContext ────────────────────────────────────────────────────

    [Fact]
    public async Task BuildContext_EmptyList_ReturnsNoDocumentsMessage() {
        var sut = CreateSut();

        var result = await sut.BuildContext([]);

        result.Should().Be("No relevant financial documents found.");
    }

    [Fact]
    public async Task BuildContext_SingleChunk_FormatsWithHeader() {
        var sut = CreateSut();
        var chunk = MakeChunk(content: "Revenue was $100M", startLineNumber: 42);

        var result = await sut.BuildContext([chunk]);

        result.Should().Contain("## Apple Inc (AAPL)");
        result.Should().Contain("10-K");
        result.Should().Contain("Revenue was $100M");
        result.Should().Contain("line ~42");
    }

    [Fact]
    public async Task BuildContext_MultipleChunksSameDocument_GroupedTogether() {
        var sut = CreateSut();
        var stock = new CommonStock { Ticker = "AAPL", Name = "Apple Inc" };
        var doc = new Document {
            CommonStock = stock, CommonStockId = stock.Id,
            DocumentType = DocumentType.TenK, ReportingDate = new DateOnly(2024, 3, 15),
        };
        var chunk1 = new Chunk {
            Document = doc, DocumentId = doc.Id, Content = "First section",
            Index = 0, StartPosition = 0, DocumentType = doc.DocumentType, Ticker = "AAPL",
        };
        var chunk2 = new Chunk {
            Document = doc, DocumentId = doc.Id, Content = "Second section",
            Index = 1, StartPosition = 100, DocumentType = doc.DocumentType, Ticker = "AAPL",
        };

        var result = await sut.BuildContext([chunk2, chunk1]);

        // Should be ordered by StartPosition
        var firstIdx = result.IndexOf("First section");
        var secondIdx = result.IndexOf("Second section");
        firstIdx.Should().BeLessThan(secondIdx);

        // Only one header for the same document
        result.Split("## Apple Inc (AAPL)").Length.Should().Be(2); // 1 occurrence = 2 parts
    }

    [Fact]
    public async Task BuildContext_ChunksFromDifferentCompanies_SeparateHeaders() {
        var sut = CreateSut();
        var appleChunk = MakeChunk(ticker: "AAPL", companyName: "Apple Inc", content: "Apple data");
        var googleChunk = MakeChunk(ticker: "GOOG", companyName: "Alphabet Inc", content: "Google data");

        var result = await sut.BuildContext([appleChunk, googleChunk]);

        result.Should().Contain("## Apple Inc (AAPL)");
        result.Should().Contain("## Alphabet Inc (GOOG)");
    }

    [Fact]
    public async Task BuildContext_WhitespaceOnlyChunk_Skipped() {
        var sut = CreateSut();
        var validChunk = MakeChunk(content: "Real content");
        var emptyChunk = MakeChunk(content: "   ");

        var result = await sut.BuildContext([validChunk, emptyChunk]);

        result.Should().Contain("Real content");
        result.Should().NotContain("Excerpt 2");
    }

    [Fact]
    public async Task BuildContext_ZeroStartLineNumber_OmitsLineReference() {
        var sut = CreateSut();
        var chunk = MakeChunk(content: "Some text", startLineNumber: 0);

        var result = await sut.BuildContext([chunk]);

        result.Should().Contain("Excerpt 1:");
        result.Should().NotContain("line ~");
    }
}
