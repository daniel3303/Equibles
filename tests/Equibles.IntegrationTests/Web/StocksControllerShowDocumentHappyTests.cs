using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.Data;
using Equibles.Media.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Equibles.Web.Controllers;
using Equibles.Web.ViewModels.Stocks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Web;

public class StocksControllerShowDocumentHappyTests
{
    [Fact]
    public async Task ShowDocument_TickerMatchesAndContentPresent_ReturnsViewWithDecodedUtf8Body()
    {
        // The only existing ShowDocument test pins the cross-ticker *guard*
        // (mismatched ticker -> NotFound). The success branch — everything
        // after the guard at lines 205-218 — has no test at all:
        //   var content = document.Content?.FileContent?.Bytes != null
        //       ? Encoding.UTF8.GetString(document.Content.FileContent.Bytes)
        //       : string.Empty;
        // A regression that broke the null-conditional chain (e.g. eager
        // `document.Content.FileContent.Bytes` throwing on a content-less
        // document) or swapped the encoding would compile, pass the guard
        // test, and silently corrupt every rendered filing. Pin: a document
        // whose bytes are UTF-8 text with a multi-byte char must round-trip
        // intact into DocumentViewModel.Content, with Ticker upper-cased.
        using var ctx = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new MediaModuleConfiguration(),
            new SecTestModuleConfiguration()
        );

        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        ctx.Set<CommonStock>().Add(stock);

        var body = "Item 1A. Risk Factors — café"u8.ToArray();
        var document = new Document
        {
            Id = Guid.NewGuid(),
            CommonStockId = stock.Id,
            CommonStock = stock,
            ContentId = Guid.NewGuid(),
            Content = new File
            {
                Name = "10k",
                Extension = "html",
                ContentType = "text/html",
                FileContent = new FileContent { Bytes = body },
            },
            DocumentType = DocumentType.TenK,
            ReportingDate = new DateOnly(2024, 1, 1),
            ReportingForDate = new DateOnly(2023, 12, 31),
        };
        ctx.Set<Document>().Add(document);
        await ctx.SaveChangesAsync();

        var sut = new StocksController(
            new CommonStockRepository(ctx),
            institutionalHolderRepository: null!,
            institutionalHoldingRepository: null!,
            new DocumentRepository(ctx),
            stockTabService: null!,
            Substitute.For<ILogger<StocksController>>()
        );

        // Lowercase ticker in the URL exercises the case-insensitive guard
        // and the `ticker.ToUpper()` assignment into the view model.
        var result = await sut.ShowDocument("aapl", document.Id);

        var view = result.Should().BeOfType<ViewResult>().Subject;
        var model = view.Model.Should().BeOfType<DocumentViewModel>().Subject;
        model.Content.Should().Be("Item 1A. Risk Factors — café");
        model.Ticker.Should().Be("AAPL");
        model.Document.Id.Should().Be(document.Id);
    }
}
