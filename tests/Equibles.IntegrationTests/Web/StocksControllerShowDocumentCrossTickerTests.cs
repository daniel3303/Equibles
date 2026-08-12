using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Media.Data;
using Equibles.Media.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Equibles.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Web;

public class StocksControllerShowDocumentCrossTickerTests
{
    [Fact]
    public async Task ShowDocument_ExistingDocumentRequestedUnderDifferentTicker_RedirectsToOwner()
    {
        // The public document is never rendered under the wrong stock. Its globally unique ID
        // sends stale ticker paths to the owning stock's canonical route.
        using var ctx = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new MediaModuleConfiguration(),
            new SecTestModuleConfiguration()
        );

        var apple = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        ctx.Set<CommonStock>().Add(apple);

        var appleDocument = new Document
        {
            Id = Guid.NewGuid(),
            CommonStockId = apple.Id,
            CommonStock = apple,
            ContentId = Guid.NewGuid(),
            Content = new File
            {
                Name = "10k",
                Extension = "html",
                ContentType = "text/html",
                FileContent = new FileContent { Bytes = "secret apple filing"u8.ToArray() },
            },
            DocumentType = DocumentType.TenK,
            ReportingDate = new DateOnly(2024, 1, 1),
            ReportingForDate = new DateOnly(2023, 12, 31),
        };
        ctx.Set<Document>().Add(appleDocument);
        await ctx.SaveChangesAsync();

        var sut = new StocksController(
            new CommonStockRepository(ctx),
            institutionalHolderRepository: null!,
            institutionalHoldingRepository: null!,
            new DocumentRepository(ctx),
            stockTabService: null!,
            Substitute.For<IFileManager>(),
            Substitute.For<ILogger<StocksController>>()
        );

        // Apple's document id, requested under a different ticker.
        var result = await sut.ShowDocument("MSFT", appleDocument.Id);

        var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
        redirect.Permanent.Should().BeTrue();
        redirect.ActionName.Should().Be(nameof(StocksController.ShowDocument));
        redirect.RouteValues.Should().ContainKey("ticker").WhoseValue.Should().Be("AAPL");
        redirect.RouteValues.Should().ContainKey("id").WhoseValue.Should().Be(appleDocument.Id);
    }
}
