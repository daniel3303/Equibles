using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Search.Abstractions;
using Equibles.Sec.BusinessLogic.Search;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Xunit;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Pins <see cref="SecDocumentSearchProvider.Search"/>'s exact-ticker behaviour: typing a ticker
/// ("ARE") must surface that company's MOST RECENT filings, not chunks that merely contain the
/// token. Before this, a content search for a short common ticker word ranked old filings heavy on
/// the word (e.g. ARE's 2002–2012 10-Ks) and the per-group cap dropped this year's filings entirely.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class SecDocumentSearchProviderTickerRecencyTests : ParadeDbMcpTestBase
{
    public SecDocumentSearchProviderTickerRecencyTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Search_ExactTicker_ReturnsCompanysMostRecentFilingsNewestFirst()
    {
        var stock = new CommonStock
        {
            Ticker = "ARE",
            Name = "Alexandria Real Estate",
            Cik = "ARE",
        };
        DbContext.Add(stock);

        // Span old → new; only the 5 newest should come back, newest first — even though the older
        // ones outnumber the cap.
        var dates = new[]
        {
            new DateOnly(2002, 3, 29),
            new DateOnly(2011, 3, 1),
            new DateOnly(2012, 2, 21),
            new DateOnly(2025, 7, 21),
            new DateOnly(2025, 12, 8),
            new DateOnly(2026, 1, 12),
            new DateOnly(2026, 1, 26),
        };
        foreach (var date in dates)
        {
            SeedDocument(stock, date);
        }
        await DbContext.SaveChangesAsync();

        var sut = new SecDocumentSearchProvider(
            HybridChunkSearcherFactory.Bm25Only(DbContext),
            new DocumentRepository(DbContext)
        );

        var group = await sut.Search(
            new SearchRequest { Query = "ARE", MaxPerProvider = 5 },
            CancellationToken.None
        );

        group
            .Hits.Select(hit => hit.Date)
            .Should()
            .ContainInOrder(
                new DateOnly(2026, 1, 26),
                new DateOnly(2026, 1, 12),
                new DateOnly(2025, 12, 8),
                new DateOnly(2025, 7, 21),
                new DateOnly(2012, 2, 21)
            );
        group.Hits.Should().HaveCount(5);
        group.Hits.Should().AllSatisfy(hit => hit.Kind.Should().Be("Filing"));
    }

    [Fact]
    public async Task Search_ExactTicker_IsCaseInsensitive()
    {
        var stock = new CommonStock
        {
            Ticker = "ARE",
            Name = "Alexandria Real Estate",
            Cik = "ARE",
        };
        DbContext.Add(stock);
        SeedDocument(stock, new DateOnly(2026, 1, 26));
        await DbContext.SaveChangesAsync();

        var sut = new SecDocumentSearchProvider(
            HybridChunkSearcherFactory.Bm25Only(DbContext),
            new DocumentRepository(DbContext)
        );

        var group = await sut.Search(
            new SearchRequest { Query = "are", MaxPerProvider = 5 },
            CancellationToken.None
        );

        group.Hits.Should().ContainSingle();
        group.Hits[0].RouteValues["ticker"].Should().Be("ARE");
    }

    private void SeedDocument(CommonStock stock, DateOnly reportingDate)
    {
        var fileContent = new Equibles.Media.Data.Models.FileContent
        {
            Bytes = "placeholder"u8.ToArray(),
        };
        var file = new File
        {
            Name = "filing",
            Extension = "txt",
            ContentType = "text/plain",
            Size = fileContent.Bytes.Length,
            FileContent = fileContent,
        };
        fileContent.FileId = file.Id;
        DbContext.Add(file);

        DbContext.Add(
            new Document
            {
                CommonStock = stock,
                CommonStockId = stock.Id,
                Content = file,
                ContentId = file.Id,
                DocumentType = DocumentType.TenK,
                ReportingDate = reportingDate,
                ReportingForDate = reportingDate.AddDays(-30),
                LineCount = 1,
            }
        );
    }
}
