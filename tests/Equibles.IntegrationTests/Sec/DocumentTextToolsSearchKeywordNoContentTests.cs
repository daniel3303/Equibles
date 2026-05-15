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
/// Sibling to <see cref="DocumentTextToolsSearchKeywordTests"/> (happy match)
/// and the Mcp-tier not-found test. This pins the third guard: a document row
/// that exists but whose file content was never populated
/// (<c>FileContent.Bytes == null</c> — e.g. a filing indexed before its body
/// downloaded). <c>SearchDocumentKeyword</c> must return the "has no content"
/// message, NOT NRE on <c>Encoding.UTF8.GetString(null)</c>. The MCP tool is
/// called directly by AI assistants; an unhandled NRE surfaces to the model as
/// an opaque tool failure instead of an actionable message.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class DocumentTextToolsSearchKeywordNoContentTests : ParadeDbMcpTestBase
{
    public DocumentTextToolsSearchKeywordNoContentTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task SearchDocumentKeyword_DocumentContentBytesNull_ReturnsHasNoContentMessage()
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

        var output = await sut.SearchDocumentKeyword(document.Id, "revenue");

        output.Should().Be($"Document {document.Id} has no content.");
    }
}
