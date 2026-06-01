using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Mcp.Tools;
using Equibles.Sec.Repositories;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Contract: ReadDocumentLines clamps the requested window (startLine to ≥1, endLine
/// to ≤totalLines); when a start beyond the document makes the clamped range empty
/// (startLine > endLine), it must return a clear "Invalid line range" message naming
/// the clamped bounds and total — never throw IndexOutOfRange or emit an empty table.
/// Sibling tests pin the clamp-and-return path; this pins the invalid-range branch.
/// Oracle derived from the clamping contract before reading the body.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class DocumentTextToolsReadLinesInvalidRangeTests : ParadeDbMcpTestBase
{
    public DocumentTextToolsReadLinesInvalidRangeTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task ReadDocumentLines_StartLineBeyondDocument_ReturnsInvalidRangeMessage()
    {
        var content = "Alpha\nBeta\nGamma"; // 3 lines total
        var stock = new CommonStock { Ticker = "MSFT", Name = "Microsoft Corp." };
        var file = new File
        {
            Name = "10q",
            Extension = "txt",
            ContentType = "text/plain",
            Size = content.Length,
            FileContent = new FileContent { Bytes = Encoding.UTF8.GetBytes(content) },
        };
        var document = new Document
        {
            CommonStock = stock,
            Content = file,
            ContentId = file.Id,
            DocumentType = DocumentType.TenQ,
            ReportingDate = new DateOnly(2025, 3, 31),
        };
        DbContext.Add(stock);
        DbContext.Add(file);
        DbContext.Add(document);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var sut = new DocumentTextTools(
            new DocumentRepository(DbContext),
            ErrorManager,
            Substitute.For<ILogger<DocumentTextTools>>()
        );

        // Request lines 5-10 in a 3-line doc: startLine (5) clamps to 5, endLine (10)
        // clamps to 3, so 5 > 3 — the window is empty and the tool must say so.
        var output = await sut.ReadDocumentLines(document.Id, startLine: 5, endLine: 10);

        output.Should().Be("Invalid line range: 5 to 3 (document has 3 lines).");
    }
}
