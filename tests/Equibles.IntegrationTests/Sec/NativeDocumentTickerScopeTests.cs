using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Xunit;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Sec;

[Collection(ParadeDbCollection.Name)]
public class NativeDocumentTickerScopeTests(ParadeDbFixture fixture) : ParadeDbMcpTestBase(fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnqualifiedTicker_KeepsUsCoRegistrantsAndExcludesForeignListingsInEverySearchArm(
        bool foreignPresentation
    )
    {
        var us = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "SAME", Name: "U.S. issuer");
        var coRegistrant = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "COREG",
            Name: "U.S. co-registrant",
            SecondaryTickers: ["SAME"]
        );
        var foreign = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "SAME",
            Name: "Lisbon issuer"
        );
        foreign.Presentation.Listing.MarketCountryCode = "PT";
        foreign.Presentation.Listing.MarketIdentifierCode = "XLIS";
        foreign.Presentation.Listing.IdentityState = EquityIdentityState.Verified;
        foreign.Presentation.Listing.TradingCurrency = "EUR";
        foreign.Presentation.Listing.QuoteUnitMultiplier = 1m;
        foreign.Presentation.Listing.IdentitySourceUrl = "https://example.test/exchange/listing";
        foreign.Presentation.Listing.IsDirectoryListed = true;
        foreign.Presentation.Listing.IsReferenceListed = true;
        if (!foreignPresentation)
            foreign.Presentation = null;
        var usChunk = AddDocument(us);
        var coRegistrantChunk = AddDocument(coRegistrant);
        var foreignChunk = AddDocument(foreign);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var documents = new DocumentRepository(DbContext);
        var expected = new[] { usChunk.DocumentId, coRegistrantChunk.DocumentId };
        (await documents.GetByTicker("same").Select(d => d.Id).ToListAsync())
            .Should()
            .BeEquivalentTo(expected);
        (await documents.GetByIssuerId(foreign.Id).Select(d => d.Id).ToListAsync())
            .Should()
            .Equal(foreignChunk.DocumentId);
        var chunks = new ChunkRepository(DbContext);
        (await chunks.HybridSearch("orbital revenue", 10, ticker: "same"))
            .Select(c => c.DocumentId)
            .Should()
            .BeEquivalentTo(expected);
        (await chunks.HybridSearchScopedFallback("orbital revenue", 10, ticker: "same"))
            .Select(c => c.DocumentId)
            .Should()
            .BeEquivalentTo(expected);
        var embeddings = new EmbeddingRepository(DbContext);
        (
            await embeddings.SearchSimilarChunks(
                [1f, 0f, 0f],
                "native-ticker-test",
                10,
                ticker: "same"
            )
        )
            .Should()
            .BeEquivalentTo(new[] { usChunk.Id, coRegistrantChunk.Id });
        (await chunks.HybridSearch("orbital revenue", 10, documentId: foreignChunk.DocumentId))
            .Should()
            .ContainSingle()
            .Which.Id.Should()
            .Be(foreignChunk.Id);
        (
            await chunks.HybridSearchScopedFallback(
                "orbital revenue",
                10,
                documentId: foreignChunk.DocumentId
            )
        )
            .Should()
            .ContainSingle()
            .Which.Id.Should()
            .Be(foreignChunk.Id);
        (
            await embeddings.SearchSimilarChunks(
                [1f, 0f, 0f],
                "native-ticker-test",
                10,
                documentId: foreignChunk.DocumentId
            )
        )
            .Should()
            .Equal(foreignChunk.Id);
    }

    private Chunk AddDocument(EquityIssuer issuer)
    {
        var file = new File
        {
            Name = "10k",
            Extension = "htm",
            ContentType = "text/html",
            Size = 1,
            FileContent = new FileContent { Bytes = [1] },
        };
        var document = new Document
        {
            Issuer = issuer,
            Content = file,
            DocumentType = DocumentType.TenK,
            ReportingDate = new DateOnly(2026, 9, 1),
            LineCount = 1,
        };
        var chunk = new Chunk
        {
            Document = document,
            Ticker = "SAME",
            DocumentType = DocumentType.TenK,
            Content = "Orbital revenue accelerated.",
            ReportingDate = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        DbContext.Add(
            new Embedding
            {
                Chunk = chunk,
                Model = "native-ticker-test",
                Vector = new Vector(new float[] { 1f, 0f, 0f }),
            }
        );
        return chunk;
    }
}
