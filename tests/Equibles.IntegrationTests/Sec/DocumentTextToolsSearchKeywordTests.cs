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
/// Unit-tier <c>DocumentTextToolsTests</c> only covers the private
/// <c>HighlightKeyword</c> helper via reflection. The public MCP tool entry points
/// (<c>SearchDocumentKeyword</c>, <c>ReadDocumentLines</c>) are uncovered. This
/// integration test pins the search happy path against real Postgres: seeded
/// document with content, single case-insensitive match, output formatted with
/// the 6-column-padded line number gutter and bold ** markers around the match.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class DocumentTextToolsSearchKeywordTests : ParadeDbMcpTestBase
{
    public DocumentTextToolsSearchKeywordTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task SearchDocumentKeyword_MatchOnLineTwoCaseInsensitive_ReturnsLineWithGutterAndBoldMarkers()
    {
        // Three lines so the match on line 2 produces both "line before" + "line after"
        // context — pins the branches at L77 (i > 0) and L87 (i < lines.Length - 1).
        var content = "First line of the filing.\n"
            + "Revenue grew 15% year-over-year.\n"
            + "Operating expenses remained stable.";
        var stock = new CommonStock { Ticker = "AAPL", Name = "Apple Inc." };
        var file = new File
        {
            Name = "10k",
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

        // Lower-case keyword against PascalCase content pins the OrdinalIgnoreCase
        // search — a regression to OrdinalCase would return "No matches found".
        var output = await sut.SearchDocumentKeyword(document.Id, "revenue");

        output.Should().Contain("1 matches found");
        output.Should().Contain("**Revenue**");          // preserves original casing inside markers
        output.Should().Contain("First line of the filing.");      // line before
        output.Should().Contain("Operating expenses remained stable."); // line after
        // Gutter is right-aligned 6-wide — drop the padding and downstream MCP clients
        // lose the column alignment they rely on for jump-to-line navigation.
        output.Should().Contain("     2 │ ");
    }
}
