using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.Errors.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Mcp.Tools;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// The other SearchDocumentKeyword pins cover not-found / no-content / happy
/// paths. This pins the catch arm: a null keyword makes
/// <c>string.Contains(null, …)</c> throw inside the scan loop, so the tool must
/// log, persist an Error via the manager, and return the safe fallback message
/// — never propagate the exception to the MCP caller.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class DocumentTextToolsSearchKeywordErrorTests : ParadeDbMcpTestBase
{
    public DocumentTextToolsSearchKeywordErrorTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task SearchDocumentKeyword_NullKeyword_LogsReportsAndReturnsFallback()
    {
        var content = "Some filing text on the only line.";
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

        var output = await sut.SearchDocumentKeyword(document.Id, keyword: null);

        output.Should().Be("An error occurred while searching the document. Please try again.");

        await using var verify = Fixture.CreateDbContext();
        var reported = await verify
            .Set<Error>()
            .AsNoTracking()
            .AnyAsync(e => e.Context == "SearchDocumentKeyword");
        reported.Should().BeTrue("the catch arm must persist the failure via the error manager");
    }
}
