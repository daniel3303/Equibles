using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Xunit;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// A company that changed its ticker keeps the filings chunked before the change under the former
/// symbol, so a search scoped to the current ticker returned only the filings chunked since.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class ChunkTickerConvergenceServiceTests(ParadeDbFixture fixture)
    : ParadeDbMcpTestBase(fixture)
{
    [Fact]
    public async Task Restamp_FilingChunkedUnderTheFormerTicker_IsFoundByTheCurrentTicker()
    {
        var renamed = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "NEWT",
            Name: "Renamed issuer"
        );
        var other = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "OTHR",
            Name: "Other issuer"
        );
        var beforeRename = AddDocument(renamed, "OLDT", new DateOnly(2024, 3, 1));
        var afterRename = AddDocument(renamed, "NEWT", new DateOnly(2026, 9, 1));
        var untouched = AddDocument(other, "OTHR", new DateOnly(2026, 9, 1));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        var chunks = new ChunkRepository(DbContext);
        var service = new ChunkTickerConvergenceService(new DocumentRepository(DbContext), chunks);

        (await chunks.HybridSearch("orbital revenue", 10, ticker: "newt"))
            .Select(chunk => chunk.DocumentId)
            .Should()
            .Equal(afterRename.DocumentId);

        var stale = await service.FindStaleDocuments(CancellationToken.None);
        stale.Should().Equal(beforeRename.DocumentId);
        (await service.Restamp(beforeRename.DocumentId, CancellationToken.None)).Should().Be(1);

        (await chunks.HybridSearch("orbital revenue", 10, ticker: "newt"))
            .Select(chunk => chunk.DocumentId)
            .Should()
            .BeEquivalentTo([beforeRename.DocumentId, afterRename.DocumentId]);
        (await service.FindStaleDocuments(CancellationToken.None)).Should().BeEmpty();
        (
            await DbContext
                .Set<Chunk>()
                .Where(chunk => chunk.DocumentId == untouched.DocumentId)
                .Select(chunk => chunk.Ticker)
                .SingleAsync()
        )
            .Should()
            .Be("OTHR");
    }

    private Chunk AddDocument(EquityIssuer issuer, string chunkTicker, DateOnly filed)
    {
        var document = new Document
        {
            Issuer = issuer,
            Content = new File
            {
                Name = "10k",
                Extension = "htm",
                ContentType = "text/html",
                Size = 1,
                FileContent = new FileContent { Bytes = [1] },
            },
            DocumentType = DocumentType.TenK,
            ReportingDate = filed,
            LineCount = 1,
        };
        var chunk = new Chunk
        {
            Document = document,
            Ticker = chunkTicker,
            DocumentType = DocumentType.TenK,
            Content = "Orbital revenue accelerated.",
            ReportingDate = filed.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
        };
        DbContext.Add(
            new Embedding
            {
                Chunk = chunk,
                Model = "chunk-ticker-test",
                Vector = new Vector(new float[] { 1f, 0f, 0f }),
            }
        );
        return chunk;
    }
}
