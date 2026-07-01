using System.Globalization;
using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Media.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Mcp.Tools;
using Equibles.Sec.Repositories;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Sec;

[Collection(ParadeDbCollection.Name)]
public class DocumentTextToolsReadDocumentLinesCultureInvarianceTests : ParadeDbMcpTestBase
{
    public DocumentTextToolsReadDocumentLinesCultureInvarianceTests(ParadeDbFixture fixture)
        : base(fixture) { }

    // ReadDocumentLines renders the line-range banner with the culture-implicit
    // :N0 specifier ("lines {startLine:N0} to {endLine:N0} of {totalLines:N0}"),
    // which honours the thread CurrentCulture. The established repo contract — the
    // McpFormat.WholeNumber helper and the dozens of InvariantCulture MCP call sites
    // commenting "MCP markdown must not fork the separators by host locale" — is that
    // the LLM-facing banner renders identically on every host. de-DE swaps the
    // thousand separator (1,500 -> 1.500), forking the response. Same bug class as
    // the fixed GetStockPrices volume cell and the GetLatestPrices repro (GH-3100).
    [Fact]
    public async Task ReadDocumentLines_UnderNonInvariantCulture_RendersLineCountsCultureInvariantly()
    {
        var content = string.Join("\n", Enumerable.Repeat("x", 1500)); // 1500 lines
        var stock = new CommonStock { Ticker = "MSFT", Name = "Microsoft Corp." };
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
            ReportingDate = new DateOnly(2025, 3, 31),
        };
        DbContext.Add(stock);
        DbContext.Add(file);
        DbContext.Add(document);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var fileManager = Substitute.For<IFileManager>();
        fileManager.GetContent(Arg.Any<File>()).Returns(ci => ((File)ci[0]).FileContent.Bytes);
        var sut = new DocumentTextTools(
            new DocumentRepository(DbContext),
            ErrorManager,
            fileManager,
            Substitute.For<ILogger<DocumentTextTools>>()
        );

        // Pin de-DE only for the rendering call; CurrentCulture flows through the
        // tool's await chain via ExecutionContext. Base class restores invariant.
        var previous = CultureInfo.CurrentCulture;
        string output;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            output = await sut.ReadDocumentLines(document.Id, startLine: 1, endLine: 1500);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        // The four-digit line counts must render with the en-US thousand comma on
        // any host locale; de-DE would produce "1.500".
        output
            .Should()
            .Contain(
                "lines 1 to 1,500 of 1,500",
                "the MCP line-range banner must not fork the thousand separator by host locale"
            );
    }
}
