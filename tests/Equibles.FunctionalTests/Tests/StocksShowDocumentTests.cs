using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using Equibles.Sec.Data.Models;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;
using File = Equibles.Media.Data.Models.File;
using FileContent = Equibles.Media.Data.Models.FileContent;

namespace Equibles.FunctionalTests.Tests;

[Collection(FunctionalTestCollection.Name)]
[Trait("Category", "Functional")]
public class StocksShowDocumentTests {
    private readonly WebAppFixture _web;
    private readonly PlaywrightFixture _playwright;

    public StocksShowDocumentTests(WebAppFixture web, PlaywrightFixture playwright) {
        _web = web;
        _playwright = playwright;
    }

    [Fact]
    public async Task ShowDocument_GetWithIdBelongingToDifferentTicker_Returns404() {
        // StocksController.ShowDocument enforces a cross-ticker guard — when the URL's ticker
        // does not match the document's owning stock it returns NotFound, even though the
        // Guid resolves to a real document. Dropping that comparison would let anyone with
        // a guessed (or leaked) document id pull SEC filings under any ticker prefix. The
        // test seeds a 10-K under AAPL and requests it under MSFT to pin the 404 boundary.
        var docId = Guid.NewGuid();
        await _web.ResetAndSeedAsync(async db => {
            var aapl = new CommonStock { Ticker = "AAPL", Name = "Apple Inc.", Cik = "0000320193" };
            var msft = new CommonStock { Ticker = "MSFT", Name = "Microsoft Corp.", Cik = "0000789019" };
            db.Add(aapl);
            db.Add(msft);

            var fileContent = new FileContent { Bytes = Encoding.UTF8.GetBytes("filler") };
            var file = new File {
                Name = "filing", Extension = "txt", ContentType = "text/plain",
                Size = fileContent.Bytes.Length, FileContent = fileContent,
            };
            fileContent.FileId = file.Id;
            db.Add(file);

            db.Add(new Document {
                Id = docId,
                CommonStock = aapl, CommonStockId = aapl.Id,
                Content = file, ContentId = file.Id,
                DocumentType = DocumentType.TenK,
                ReportingDate = new DateOnly(2025, 3, 15),
                ReportingForDate = new DateOnly(2024, 12, 31),
                LineCount = 1,
            });
            await Task.CompletedTask;
        });

        var page = await _playwright.NewPageAsync(_web.BaseUrl);
        var response = await page.GotoAsync($"/stocks/msft/documents/{docId}");

        response.Should().NotBeNull();
        response!.Status.Should().Be(404,
            "ShowDocument must reject mismatched ticker even when the document id resolves");
    }
}
