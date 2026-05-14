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
/// Sibling to <see cref="DocumentTextToolsSearchKeywordTests"/>. Pins the other MCP
/// tool, <c>ReadDocumentLines</c>: out-of-range <c>endLine</c> must clamp to the
/// document length rather than throw IndexOutOfRange, and the returned text must
/// carry the gutter-formatted lines from <c>startLine</c> through the clamped end.
/// A regression that dropped <c>Math.Min(totalLines, endLine)</c> would crash MCP
/// clients on every request that overshoots a short filing.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class DocumentTextToolsReadLinesTests : ParadeDbMcpTestBase
{
    public DocumentTextToolsReadLinesTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task ReadDocumentLines_EndLineExceedsDocumentLength_ClampsToTotalAndReturnsRemainingLines()
    {
        var content = "Alpha\nBeta\nGamma";  // 3 lines total
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

        // Request lines 2-99 in a 3-line doc — clamps endLine to 3.
        var output = await sut.ReadDocumentLines(document.Id, startLine: 2, endLine: 99);

        // Header reports the clamped range: "lines 2 to 3 of 3".
        output.Should().Contain("lines 2 to 3 of 3");
        output.Should().Contain("     2 │ Beta");
        output.Should().Contain("     3 │ Gamma");
        output.Should().NotContain("Alpha");  // startLine=2 excludes line 1
    }
}
