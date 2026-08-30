using System.Globalization;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Media.Data.Models;
using Equibles.Sec.BusinessLogic.Search;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Mcp.Tools;
using Equibles.Sec.Repositories;
using NSubstitute;
using Xunit;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Mcp;

[Collection(ParadeDbCollection.Name)]
public class RagSearchToolsListFilingsCultureInvarianceTests : ParadeDbMcpTestBase
{
    public RagSearchToolsListFilingsCultureInvarianceTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private RagSearchTools Sut()
    {
        var ragManager = new RagManager(
            HybridChunkSearcherFactory.Bm25Only(DbContext),
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
            new CommonStockRepository(DbContext),
            new DocumentRepository(DbContext),
            Substitute.For<IFileManager>(),
            ErrorManager,
            NullLogger<RagSearchTools>()
        );
    }

    [Fact]
    public async Task ListFilings_UnderNonGregorianCulture_RendersDatesAndCountsInvariantly()
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

        var previous = CultureInfo.CurrentCulture;
        string result;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
            result = await Sut().ListFilings("AAPL");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        result.Should().Contain("2026-03-15 | 2026-02-15");
        result
            .Should()
            .Contain("| 1,500", "the MCP filing table must not fork numbers by host locale");
    }
}
