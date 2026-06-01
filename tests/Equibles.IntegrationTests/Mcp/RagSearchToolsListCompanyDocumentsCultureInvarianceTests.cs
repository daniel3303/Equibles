using System.Globalization;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.Data.Models;
using Equibles.Sec.BusinessLogic.Search;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Mcp.Tools;
using Equibles.Sec.Repositories;
using Xunit;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Mcp;

[Collection(ParadeDbCollection.Name)]
public class RagSearchToolsListCompanyDocumentsCultureInvarianceTests : ParadeDbMcpTestBase
{
    public RagSearchToolsListCompanyDocumentsCultureInvarianceTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private RagSearchTools Sut()
    {
        var ragManager = new RagManager(
            new ChunkRepository(DbContext),
            new CommonStockRepository(DbContext),
            NullLogger<RagManager>()
        );
        var secDocumentService = new SecDocumentService(
            new DocumentRepository(DbContext),
            NullLogger<SecDocumentService>()
        );
        return new RagSearchTools(
            ragManager,
            secDocumentService,
            ErrorManager,
            NullLogger<RagSearchTools>()
        );
    }

    // Contract (the repo-wide MCP rule, enforced by McpFormat and the dozens of InvariantCulture
    // MCP call sites that comment "MCP markdown must not fork the separators by host locale"):
    // LLM-facing markdown must render numbers identically on every host locale. The
    // ListCompanyDocuments table builds the Lines cell with the culture-implicit :N0 specifier
    // (RagSearchTools.cs:174), which honours the thread CurrentCulture — de-DE swaps the thousand
    // separator (1,500 -> 1.500), forking the response by host locale. Same bug class as the
    // already-pinned ReadDocumentLines (GH-3110) and GetLatestPrices (GH-3100) repros.
    [Fact(
        Skip = "GH-3114 — ListCompanyDocuments forks the Lines column :N0 formatting by host locale"
    )]
    public async Task ListCompanyDocuments_UnderNonInvariantCulture_RendersLineCountCultureInvariantly()
    {
        var stock = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc",
            Cik = "0000320193",
        };
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
        var document = new Document
        {
            CommonStock = stock,
            CommonStockId = stock.Id,
            Content = file,
            ContentId = file.Id,
            DocumentType = DocumentType.TenK,
            ReportingDate = new DateOnly(2026, 3, 15),
            ReportingForDate = new DateOnly(2026, 2, 15),
            LineCount = 1500, // four-digit count so the thousand separator differs across locales
        };
        DbContext.Add(stock);
        DbContext.Set<File>().Add(file);
        DbContext.Set<Document>().Add(document);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        // Pin de-DE only for the rendering call; CurrentCulture flows through the tool's await
        // chain via ExecutionContext. Base class restores invariant.
        var previous = CultureInfo.CurrentCulture;
        string result;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            result = await Sut().ListCompanyDocuments("AAPL");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        // The Lines cell must render with the en-US thousand comma on any host locale;
        // de-DE would produce "| 1.500".
        result
            .Should()
            .Contain(
                "| 1,500",
                "the MCP document-listing table must not fork the thousand separator by host locale"
            );
    }
}
