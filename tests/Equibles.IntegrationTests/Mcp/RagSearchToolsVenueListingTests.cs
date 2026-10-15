using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Media.Data.Models;
using Equibles.Sec.BusinessLogic.Search;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.Mcp.Tools;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Mcp;

// A venue symbol scopes document search and listing to its issuer's documents. Chunks keep the
// presentation ticker they were cut under, so Airbus's chunks share AAR's "AIR" and HSBC's Hong
// Kong listing finds chunks stored under "HSBC"; neither may leak into the other's results.
[Collection(ParadeDbCollection.Name)]
public class RagSearchToolsVenueListingTests : ParadeDbMcpTestBase
{
    public RagSearchToolsVenueListingTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private RagSearchTools Sut() =>
        new(
            new RagManager(
                HybridChunkSearcherFactory.Bm25Only(DbContext),
                new EquityIssuerRepository(DbContext),
                NullLogger<RagManager>()
            ),
            new SecDocumentService(
                new DocumentRepository(DbContext),
                NullLogger<SecDocumentService>()
            ),
            new EquityIssuerRepository(DbContext),
            new DocumentRepository(DbContext),
            Substitute.For<IFileManager>(),
            ErrorManager,
            NullLogger<RagSearchTools>()
        );

    [Fact]
    public async Task SearchDocuments_VenueSymbol_ReadsOnlyThatIssuersDocuments()
    {
        await SeedIssuers();

        var result = await Sut().SearchDocuments("revenue", "XPAR:AIR");

        result.Should().Contain("Airbus SE (XPAR:AIR)").And.Contain("aircraft deliveries");
        result.Should().NotContain("AAR Corp").And.NotContain("repair stations");
    }

    [Fact]
    public async Task SearchDocuments_BareTicker_StaysTheUsFiler()
    {
        await SeedIssuers();

        var result = await Sut().SearchDocuments("revenue", "AIR");

        result.Should().Contain("AAR Corp (AIR)").And.Contain("repair stations");
        result.Should().NotContain("Airbus").And.NotContain("aircraft deliveries");
    }

    // HSBC's documents were chunked under its US presentation ticker, never "00005".
    [Fact]
    public async Task SearchDocuments_VenueListingOfAUsPresentedIssuer_FindsItsDocuments()
    {
        await SeedIssuers();

        var padded = await Sut().SearchDocuments("revenue", "XHKG:5");
        var phrase = await Sut()
            .SearchDocuments("net interest income", "XHKG:00005", match: "phrase");

        padded.Should().Contain("Hsbc Holdings PLC (HSBC)").And.Contain("Net interest income");
        phrase.Should().Contain("Hsbc Holdings PLC (HSBC)");
        padded.Should().NotContain("Airbus").And.NotContain("AAR Corp");
    }

    [Fact]
    public async Task SearchDocuments_ProviderSuffix_ResolvesTheVenueAfterAUsMiss()
    {
        await SeedIssuers();

        var result = await Sut().SearchDocuments("revenue", "AIR.PA");

        result.Should().Contain("Airbus SE (XPAR:AIR)").And.NotContain("AAR Corp");
    }

    [Fact]
    public async Task SearchDocuments_VenueDateWindowWithoutDocuments_FindsNothing()
    {
        await SeedIssuers();

        var result = await Sut()
            .SearchDocuments("revenue", "XPAR:AIR", startDate: new DateTime(2026, 6, 1));

        result.Should().Be("No relevant financial documents found.");
    }

    [Fact]
    public async Task SearchDocuments_UnknownVenueListing_SaysSo()
    {
        await SeedIssuers();

        var result = await Sut().SearchDocuments("revenue", "XPAR:ZZZZ");

        result.Should().StartWith("Listing 'XPAR:ZZZZ' not found.");
    }

    [Fact]
    public async Task ListFilings_VenueSymbol_ListsThatIssuerUnderItsDisplaySymbol()
    {
        await SeedIssuers();

        var result = await Sut().ListFilings("XPAR:AIR");

        result.Should().Contain("Financial documents for Airbus SE (XPAR:AIR)");
        result.Should().Contain("XPAR:AIR | Airbus SE |").And.NotContain("AAR Corp");
    }

    // A market-wide feed must never print Airbus's filings under AAR's bare ticker, and keeps ESEF
    // reports out unless asked for.
    [Fact]
    public async Task ListFilings_MarketWide_LabelsVenuePresentedFilersByDisplaySymbol()
    {
        await SeedIssuers();

        var unfiltered = await Sut().ListFilings();
        var esef = await Sut().ListFilings(documentType: "EsefAnnualReport");

        unfiltered.Should().Contain("AIR | AAR Corp |").And.Contain("HSBC | Hsbc Holdings PLC |");
        unfiltered.Should().NotContain("Airbus SE");
        esef.Should().Contain("XPAR:AIR | Airbus SE |").And.NotContain("| AIR | Airbus SE");
    }

    [Fact]
    public async Task ListFilings_UsTicker_StillHidesEsefReports()
    {
        await SeedIssuers();
        AddDocument(
            await DbContext
                .Set<EquityIssuer>()
                .SingleAsync(issuer => issuer.Name == "Hsbc Holdings PLC"),
            "HSBC",
            DocumentType.EsefAnnualReport,
            "Annual financial report."
        );
        await DbContext.SaveChangesAsync();

        var us = await Sut().ListFilings("HSBC");
        var venue = await Sut().ListFilings("XHKG:00005");

        us.Should().Contain("(1 documents)").And.NotContain("ESEF");
        venue
            .Should()
            .Contain("Financial documents for Hsbc Holdings PLC (XHKG:00005)")
            .And.Contain("(2 documents)");
    }

    private async Task SeedIssuers()
    {
        var aar = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "AIR",
            Name: "AAR Corp",
            Cik: "0000001750"
        );
        var airbus = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "AIR",
            Name: "Airbus SE",
            MarketCountryCode: "FR",
            MarketIdentifierCode: "XPAR",
            IdentityState: EquityIdentityState.Verified,
            TradingCurrency: "EUR",
            QuoteUnitMultiplier: 1m,
            Isin: "NL0000235190"
        );
        airbus.Presentation.Listing.IdentitySourceUrl = "https://example.test/euronext";
        var hsbc = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "HSBC",
            Name: "Hsbc Holdings PLC",
            Cik: "0001089113"
        );
        var hongKong = new EquitySecurity
        {
            Issuer = hsbc,
            EquityIssuerId = hsbc.Id,
            Isin = "GB0005405286",
        };
        hongKong.Listings.Add(
            new EquityListing
            {
                Security = hongKong,
                EquitySecurityId = hongKong.Id,
                Ticker = "00005",
                MarketCountryCode = "HK",
                MarketIdentifierCode = "XHKG",
                IdentityState = EquityIdentityState.Verified,
                IdentitySourceUrl = "https://example.test/hkex",
                TradingCurrency = "HKD",
                QuoteUnitMultiplier = 1m,
                Active = true,
            }
        );
        hsbc.Securities.Add(hongKong);
        DbContext.Set<EquityIssuer>().AddRange(aar, airbus, hsbc);
        await DbContext.SaveChangesAsync();

        AddDocument(
            aar,
            "AIR",
            DocumentType.TenK,
            "Aviation services revenue grew at the repair stations."
        );
        AddDocument(
            airbus,
            "AIR",
            DocumentType.EsefAnnualReport,
            "Commercial aircraft deliveries lifted revenue."
        );
        AddDocument(
            hsbc,
            "HSBC",
            DocumentType.TwentyF,
            "Net interest income drove revenue in Hong Kong."
        );
        await DbContext.SaveChangesAsync();
    }

    private void AddDocument(
        EquityIssuer issuer,
        string chunkTicker,
        DocumentType type,
        string content
    )
    {
        var reportingDate = new DateOnly(2026, 3, 15);
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
            Issuer = issuer,
            Content = file,
            ContentId = file.Id,
            DocumentType = type,
            ReportingDate = reportingDate,
            ReportingForDate = reportingDate.AddDays(-75),
            LineCount = 1,
        };
        DbContext.Set<File>().Add(file);
        DbContext.Set<Document>().Add(document);
        DbContext
            .Set<Chunk>()
            .Add(
                new Chunk
                {
                    Document = document,
                    DocumentId = document.Id,
                    Index = 0,
                    StartPosition = 0,
                    EndPosition = content.Length,
                    StartLineNumber = 1,
                    Content = content,
                    DocumentType = type,
                    Ticker = chunkTicker,
                    ReportingDate = DateTime.SpecifyKind(
                        reportingDate.ToDateTime(TimeOnly.MinValue),
                        DateTimeKind.Utc
                    ),
                }
            );
    }
}
