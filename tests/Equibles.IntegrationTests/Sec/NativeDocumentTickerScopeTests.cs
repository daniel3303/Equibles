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
        us.Presentation.Listing.IsDirectoryListed = false;
        us.Presentation.Listing.IsReferenceListed = false;
        coRegistrant.Presentation = null;
        var unlisted = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "SAME",
            Name: "Unlisted issuer"
        );
        unlisted.Presentation.Listing.IsDirectoryListed = false;
        unlisted.Presentation.Listing.IsReferenceListed = false;
        unlisted.Presentation = null;
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
        AddDocument(unlisted);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var documents = new DocumentRepository(DbContext);
        documents
            .GetByTicker("same")
            .ToQueryString()
            .Should()
            .Contain(
                "UNION",
                "ticker claims must be resolved before the filing-date scan rather than correlated per document"
            );
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

    // MIC:TICKER selects its issuer's documents in every arm, whatever ticker their chunks were cut
    // under: the Lisbon chunks share the US issuer's "SAME", and the dual listing's carry "DUAL".
    [Fact]
    public async Task VenueSymbol_SelectsItsIssuersDocumentsInEverySearchArm()
    {
        var us = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "SAME", Name: "U.S. issuer");
        var lisbon = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "SAME",
            Name: "Lisbon issuer",
            MarketCountryCode: "PT",
            MarketIdentifierCode: "XLIS",
            IdentityState: EquityIdentityState.Verified,
            TradingCurrency: "EUR",
            QuoteUnitMultiplier: 1m
        );
        lisbon.Presentation.Listing.IdentitySourceUrl = "https://example.test/exchange/listing";
        var dual = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "DUAL",
            Name: "Dual issuer"
        );
        var hongKong = new EquitySecurity { Issuer = dual, EquityIssuerId = dual.Id };
        hongKong.Listings.Add(
            new EquityListing
            {
                Security = hongKong,
                EquitySecurityId = hongKong.Id,
                Ticker = "00777",
                MarketCountryCode = "HK",
                MarketIdentifierCode = "XHKG",
                IdentityState = EquityIdentityState.Verified,
                IdentitySourceUrl = "https://example.test/exchange/hk",
                TradingCurrency = "HKD",
                QuoteUnitMultiplier = 1m,
                Active = true,
            }
        );
        dual.Securities.Add(hongKong);
        var usChunk = AddDocument(us);
        var lisbonChunk = AddDocument(lisbon);
        var dualChunk = AddDocument(dual, "DUAL");
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var documents = new DocumentRepository(DbContext);
        var chunks = new ChunkRepository(DbContext);
        var embeddings = new EmbeddingRepository(DbContext);
        foreach (
            var (symbol, expected) in new[]
            {
                ("XLIS:SAME", lisbonChunk),
                ("XHKG:00777", dualChunk),
            }
        )
        {
            (await documents.GetByTicker(symbol).Select(d => d.Id).ToListAsync())
                .Should()
                .Equal(expected.DocumentId);
            (await chunks.HybridSearch("orbital revenue", 10, ticker: symbol))
                .Select(c => c.Id)
                .Should()
                .Equal(expected.Id);
            (await chunks.HybridSearchScopedFallback("orbital revenue", 10, ticker: symbol))
                .Select(c => c.Id)
                .Should()
                .Equal(expected.Id);
            (
                await embeddings.SearchSimilarChunks(
                    [1f, 0f, 0f],
                    "native-ticker-test",
                    10,
                    ticker: symbol
                )
            )
                .Should()
                .Equal(expected.Id);
        }
        (await documents.GetByTicker("SAME").Select(d => d.Id).ToListAsync())
            .Should()
            .Equal(usChunk.DocumentId);
    }

    // Only an active, verified listing outside the US is a venue scope; the rest select nothing.
    [Fact]
    public async Task VenueSymbol_InactiveUnverifiedOrUsListing_SelectsNoDocuments()
    {
        var inactive = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "GONE",
            Name: "Inactive issuer",
            Active: false,
            MarketCountryCode: "FR",
            MarketIdentifierCode: "XPAR",
            IdentityState: EquityIdentityState.Verified,
            TradingCurrency: "EUR",
            QuoteUnitMultiplier: 1m
        );
        inactive.Presentation.Listing.IdentitySourceUrl = "https://example.test/exchange/gone";
        var unverified = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "OLDV",
            Name: "Unverified issuer",
            MarketCountryCode: "FR",
            MarketIdentifierCode: "XPAR",
            TradingCurrency: "EUR",
            QuoteUnitMultiplier: 1m
        );
        var us = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "HOME", Name: "U.S. issuer");
        us.Presentation.Listing.MarketIdentifierCode = "XNYS";
        us.Presentation.Listing.IdentityState = EquityIdentityState.Verified;
        us.Presentation.Listing.IdentitySourceUrl = "https://example.test/exchange/home";
        us.Presentation.Listing.TradingCurrency = "USD";
        us.Presentation.Listing.QuoteUnitMultiplier = 1m;
        AddDocument(inactive, "GONE");
        AddDocument(unverified, "OLDV");
        AddDocument(us, "HOME");
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var documents = new DocumentRepository(DbContext);
        var chunks = new ChunkRepository(DbContext);
        foreach (var symbol in new[] { "XPAR:GONE", "XPAR:OLDV", "XNYS:HOME" })
        {
            (await documents.GetByTicker(symbol).ToListAsync()).Should().BeEmpty(symbol);
            (await chunks.HybridSearch("orbital revenue", 10, ticker: symbol))
                .Should()
                .BeEmpty(symbol);
        }
    }

    private Chunk AddDocument(EquityIssuer issuer, string chunkTicker = "SAME")
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
            Ticker = chunkTicker,
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
