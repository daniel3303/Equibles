using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Pins <see cref="DocumentRepository.GetByTicker"/>: the SecondaryTickers
/// branch resolves documents for parent/subsidiary stocks that share a ticker
/// (e.g. preferred-share tickers listed under both the parent REIT and its
/// operating-partnership SEC filer). The branch uses
/// <c>SecondaryTickers.Contains(ticker.ToUpper())</c> against a Postgres
/// <c>text[]</c> column — InMemory provider can't represent this. A regression
/// that dropped the secondary branch would silently miss documents on every
/// preferred/co-registrant ticker query.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class DocumentRepositoryGetByTickerTests : ParadeDbMcpTestBase
{
    public DocumentRepositoryGetByTickerTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetByTicker_QueryMatchesSecondaryTicker_ReturnsDocumentViaSecondaryArrayContains()
    {
        // Parent stock with primary AAPL and a co-registrant ticker AAPL.PRA on
        // SecondaryTickers. The doc is filed against the parent, but a query for
        // the secondary ticker must still find it.
        var parent = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            SecondaryTickers = ["AAPL.PRA"],
        };
        var file = new File
        {
            Name = "10k",
            Extension = "htm",
            ContentType = "text/html",
            Size = 100,
            FileContent = new FileContent { Bytes = new byte[] { 0x01 } },
        };
        var document = new Document
        {
            CommonStock = parent,
            Content = file,
            ContentId = file.Id,
            DocumentType = DocumentType.TenK,
            ReportingDate = new DateOnly(2025, 1, 15),
        };
        DbContext.Add(parent);
        DbContext.Add(file);
        DbContext.Add(document);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        // Query in mixed case — the .ToUpper() inside the production filter must
        // normalise before hitting the text[]; a regression that dropped the
        // ToUpper would silently miss real-world queries from lower-cased input.
        await using var verify = Fixture.CreateDbContext();
        var sut = new DocumentRepository(verify);

        var docs = await sut.GetByTicker("aapl.pra").AsNoTracking().ToListAsync();

        docs.Should().ContainSingle();
        docs[0].DocumentType.Should().Be(DocumentType.TenK);
        docs[0].CommonStock.Ticker.Should().Be("AAPL");
    }
}
