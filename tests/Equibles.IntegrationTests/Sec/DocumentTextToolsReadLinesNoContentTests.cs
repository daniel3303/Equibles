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
/// Sibling to <see cref="DocumentTextToolsSearchKeywordNoContentTests"/>, which
/// pins the same guard on the OTHER MCP tool. ReadDocumentLines' no-content
/// branch was zero-hit by the whole suite. A document indexed before its body
/// downloaded (<c>FileContent.Bytes == null</c>) must yield the "has no content"
/// message, NOT an NRE on <c>Encoding.UTF8.GetString(null)</c> — the tool is
/// called directly by AI assistants and an unhandled NRE is an opaque failure.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class DocumentTextToolsReadLinesNoContentTests : ParadeDbMcpTestBase
{
    public DocumentTextToolsReadLinesNoContentTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task ReadDocumentLines_DocumentContentBytesNull_ReturnsHasNoContentMessage()
    {
        var stock = new CommonStock { Ticker = "AAPL", Name = "Apple Inc." };
        var file = new File
        {
            Name = "10k",
            Extension = "txt",
            ContentType = "text/plain",
            Size = 0,
            // Indexed-but-not-yet-downloaded: the FileContent row exists, Bytes is null.
            FileContent = new FileContent { Bytes = null },
        };
        var document = new Document
        {
            CommonStock = stock,
            Content = file,
            ContentId = file.Id,
            DocumentType = DocumentType.TenK,
            ReportingDate = new DateOnly(2025, 1, 15),
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

        var output = await sut.ReadDocumentLines(document.Id, startLine: 1, endLine: 10);

        output.Should().Be($"Document {document.Id} has no content.");
    }
}
