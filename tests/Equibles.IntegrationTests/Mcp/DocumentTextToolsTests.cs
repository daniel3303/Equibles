using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Mcp.Tools;
using Equibles.Sec.Repositories;
using Xunit;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Mcp;

[Collection(ParadeDbCollection.Name)]
public class DocumentTextToolsTests : ParadeDbMcpTestBase {
    public DocumentTextToolsTests(ParadeDbFixture fixture) : base(fixture) { }

    private DocumentTextTools Sut() => new(
        new DocumentRepository(DbContext),
        ErrorManager,
        NullLogger<DocumentTextTools>());

    private async Task<Document> SeedDocument(string content, string ticker = "AAPL", string companyName = "Apple Inc") {
        var stock = new CommonStock {
            Ticker = ticker, Name = companyName,
            Cik = Random.Shared.NextInt64(1_000_000_000L, 9_999_999_999L).ToString(),
        };
        DbContext.Set<CommonStock>().Add(stock);

        var fileContent = new FileContent { Bytes = Encoding.UTF8.GetBytes(content) };
        var file = new File {
            Name = "filing", Extension = "txt", ContentType = "text/plain",
            Size = fileContent.Bytes.Length, FileContent = fileContent,
        };
        fileContent.FileId = file.Id;
        DbContext.Set<File>().Add(file);

        var document = new Document {
            CommonStock = stock, CommonStockId = stock.Id,
            Content = file, ContentId = file.Id,
            DocumentType = DocumentType.TenK,
            ReportingDate = new DateOnly(2025, 3, 15),
            ReportingForDate = new DateOnly(2024, 12, 31),
            LineCount = content.Split('\n').Length,
        };
        DbContext.Set<Document>().Add(document);
        await DbContext.SaveChangesAsync();
        return document;
    }

    // ── SearchDocumentKeyword ───────────────────────────────────────────

    [Fact]
    public async Task SearchDocumentKeyword_KeywordFound_ReturnsMatchesWithContext() {
        var doc = await SeedDocument("Line one\nRevenue was $100M\nLine three\nRevenue increased\nLine five");

        var result = await Sut().SearchDocumentKeyword(doc.Id, "Revenue");

        result.Should().Contain("Revenue");
        result.Should().Contain("2 matches found");
        result.Should().Contain("**Revenue**");
    }

    [Fact]
    public async Task SearchDocumentKeyword_CaseInsensitive_FindsMatches() {
        var doc = await SeedDocument("First line\nTotal REVENUE was high\nLast line");

        var result = await Sut().SearchDocumentKeyword(doc.Id, "revenue");

        result.Should().Contain("1 matches found");
        result.Should().Contain("**REVENUE**");
    }

    [Fact]
    public async Task SearchDocumentKeyword_NotFound_ReturnsNoMatchesMessage() {
        var doc = await SeedDocument("Line one\nLine two\nLine three");

        var result = await Sut().SearchDocumentKeyword(doc.Id, "nonexistent");

        result.Should().Contain("No matches found for \"nonexistent\"");
        result.Should().Contain("Apple Inc (AAPL)");
    }

    [Fact]
    public async Task SearchDocumentKeyword_DocumentNotFound_ReturnsNotFoundMessage() {
        var missingId = Guid.NewGuid();

        var result = await Sut().SearchDocumentKeyword(missingId, "test");

        result.Should().Contain($"Document {missingId} not found.");
    }

    [Fact]
    public async Task SearchDocumentKeyword_MaxResultsRespected_LimitsMatches() {
        var lines = Enumerable.Range(1, 50).Select(i => $"Revenue line {i}").ToArray();
        var doc = await SeedDocument(string.Join("\n", lines));

        var result = await Sut().SearchDocumentKeyword(doc.Id, "Revenue", maxResults: 3);

        result.Should().Contain("3 matches found");
    }

    [Fact]
    public async Task SearchDocumentKeyword_IncludesContextLines() {
        var doc = await SeedDocument("Before line\nTarget keyword here\nAfter line");

        var result = await Sut().SearchDocumentKeyword(doc.Id, "keyword");

        result.Should().Contain("Before line");
        result.Should().Contain("**keyword**");
        result.Should().Contain("After line");
    }

    [Fact]
    public async Task SearchDocumentKeyword_IncludesDocumentMetadata() {
        var doc = await SeedDocument("Some keyword content", ticker: "MSFT", companyName: "Microsoft Corp");

        var result = await Sut().SearchDocumentKeyword(doc.Id, "keyword");

        result.Should().Contain("Microsoft Corp (MSFT)");
        result.Should().Contain("10-K");
        result.Should().Contain("2025-03-15");
    }

    // ── ReadDocumentLines ───────────────────────────────────────────────

    [Fact]
    public async Task ReadDocumentLines_ValidRange_ReturnsNumberedLines() {
        var doc = await SeedDocument("Line 1\nLine 2\nLine 3\nLine 4\nLine 5");

        var result = await Sut().ReadDocumentLines(doc.Id, 2, 4);

        result.Should().Contain("Line 2");
        result.Should().Contain("Line 3");
        result.Should().Contain("Line 4");
        result.Should().NotContain("Line 1");
        result.Should().NotContain("Line 5");
        result.Should().Contain("lines 2 to 4 of 5");
    }

    [Fact]
    public async Task ReadDocumentLines_EntireDocument_ReturnsAllLines() {
        var doc = await SeedDocument("Alpha\nBravo\nCharlie");

        var result = await Sut().ReadDocumentLines(doc.Id, 1, 3);

        result.Should().Contain("Alpha");
        result.Should().Contain("Bravo");
        result.Should().Contain("Charlie");
        result.Should().Contain("lines 1 to 3 of 3");
    }

    [Fact]
    public async Task ReadDocumentLines_StartLineBelowOne_ClampedToOne() {
        var doc = await SeedDocument("Line 1\nLine 2\nLine 3");

        var result = await Sut().ReadDocumentLines(doc.Id, -5, 2);

        result.Should().Contain("Line 1");
        result.Should().Contain("Line 2");
        result.Should().Contain("lines 1 to 2 of 3");
    }

    [Fact]
    public async Task ReadDocumentLines_EndLineBeyondTotal_ClampedToTotal() {
        var doc = await SeedDocument("Line 1\nLine 2\nLine 3");

        var result = await Sut().ReadDocumentLines(doc.Id, 2, 100);

        result.Should().Contain("Line 2");
        result.Should().Contain("Line 3");
        result.Should().Contain("lines 2 to 3 of 3");
    }

    [Fact]
    public async Task ReadDocumentLines_StartAfterEnd_ReturnsInvalidRangeMessage() {
        var doc = await SeedDocument("Line 1\nLine 2\nLine 3");

        var result = await Sut().ReadDocumentLines(doc.Id, 5, 2);

        result.Should().Contain("Invalid line range");
    }

    [Fact]
    public async Task ReadDocumentLines_DocumentNotFound_ReturnsNotFoundMessage() {
        var missingId = Guid.NewGuid();

        var result = await Sut().ReadDocumentLines(missingId, 1, 10);

        result.Should().Contain($"Document {missingId} not found.");
    }

    [Fact]
    public async Task ReadDocumentLines_IncludesDocumentMetadata() {
        var doc = await SeedDocument("Content here", ticker: "GOOG", companyName: "Alphabet Inc");

        var result = await Sut().ReadDocumentLines(doc.Id, 1, 1);

        result.Should().Contain("Alphabet Inc (GOOG)");
        result.Should().Contain("10-K");
        result.Should().Contain("2025-03-15");
    }

    [Fact]
    public async Task ReadDocumentLines_LinesAreNumbered() {
        var doc = await SeedDocument("Alpha\nBravo\nCharlie");

        var result = await Sut().ReadDocumentLines(doc.Id, 1, 3);

        result.Should().Contain("1 │ Alpha");
        result.Should().Contain("2 │ Bravo");
        result.Should().Contain("3 │ Charlie");
    }

    [Fact]
    public async Task ReadDocumentLines_SingleLine_ReturnsOneLine() {
        var doc = await SeedDocument("Line 1\nLine 2\nLine 3");

        var result = await Sut().ReadDocumentLines(doc.Id, 2, 2);

        result.Should().Contain("Line 2");
        result.Should().Contain("lines 2 to 2 of 3");
        result.Should().NotContain("Line 1");
        result.Should().NotContain("Line 3");
    }
}
