using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.HostedService.Services;
using Equibles.Sec.FinancialFacts.Repositories;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// End-to-end pin of the dimensional-fact extraction path (#877): a captured
/// gzipped inline-XBRL envelope goes in, dimensional FinancialFact rows with
/// FinancialFactDimension children come out, and the consolidated fact in the
/// same envelope is left to the Company Facts API (not persisted). Also pins
/// idempotency — re-extracting the same document must not duplicate rows,
/// which exercises the DimensionsKey conflict target end to end against real
/// Postgres.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class XbrlFactExtractionServiceExtractTests : ParadeDbMcpTestBase
{
    private const string Accession = "0000320193-25-000001";

    public XbrlFactExtractionServiceExtractTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Extract_CapturedInlineEnvelope_PersistsDimensionalFactWithChildAndSkipsConsolidated()
    {
        var document = await SeedDocument(InlineEnvelope());
        var sut = BuildSut();

        var persisted = await sut.Extract(document, CancellationToken.None);

        persisted.Should().Be(1);

        var facts = await DbContext
            .Set<FinancialFact>()
            .Include(f => f.Dimensions)
            .Include(f => f.FinancialConcept)
            .Where(f => f.DocumentId == document.Id)
            .ToListAsync(CancellationToken.None);

        var fact = facts.Should().ContainSingle().Subject;
        fact.FinancialConcept.Taxonomy.Should().Be(FactTaxonomy.UsGaap);
        fact.FinancialConcept.Tag.Should()
            .Be("RevenueFromContractWithCustomerExcludingAssessedTax");
        fact.Unit.Should().Be("USD");
        fact.Value.Should().Be(46_222_000_000m);
        fact.AccessionNumber.Should().Be(Accession);
        // Dec-31 FYE + Jan–Mar duration resolves to Q1 2025.
        fact.FiscalYear.Should().Be(2025);
        fact.FiscalPeriod.Should().Be(SecFiscalPeriod.Q1);
        fact.DimensionsKey.Should().MatchRegex("^[0-9a-f]{64}$");

        var dimension = fact.Dimensions.Should().ContainSingle().Subject;
        dimension.Axis.Should().Be("srt:ProductOrServiceAxis");
        dimension.Member.Should().Be("aapl:IPhoneMember");

        // Idempotency: a second sweep over the same envelope changes nothing.
        var secondRun = await sut.Extract(document, CancellationToken.None);
        secondRun.Should().Be(1);
        (
            await DbContext
                .Set<FinancialFact>()
                .CountAsync(f => f.DocumentId == document.Id, CancellationToken.None)
        )
            .Should()
            .Be(1);
        (
            await DbContext
                .Set<FinancialFactDimension>()
                .CountAsync(d => d.FinancialFact.DocumentId == document.Id, CancellationToken.None)
        )
            .Should()
            .Be(1);
    }

    private XbrlFactExtractionService BuildSut()
    {
        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(EquiblesFinancialDbContext), DbContext),
            (typeof(FinancialConceptRepository), new FinancialConceptRepository(DbContext))
        );
        var fileManager = Substitute.For<IFileManager>();
        fileManager.GetContent(Arg.Any<File>()).Returns(ci => ((File)ci[0]).FileContent.Bytes);
        return new XbrlFactExtractionService(
            scopeFactory,
            new InlineXbrlParser(),
            new StandaloneXbrlParser(),
            fileManager,
            NullLogger<XbrlFactExtractionService>()
        );
    }

    private async Task<Document> SeedDocument(string envelope)
    {
        var stock = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
            FiscalYearEndMonth = 12,
            FiscalYearEndDay = 31,
        };

        var compressed = GzipCompressor.Compress(System.Text.Encoding.UTF8.GetBytes(envelope));
        var xbrlFile = new File
        {
            Name = "xbrl-envelope",
            Extension = "gz",
            ContentType = "application/gzip",
            Size = compressed.Length,
            FileContent = new Equibles.Media.Data.Models.FileContent { Bytes = compressed },
        };
        var contentFile = new File
        {
            Name = "primary-doc",
            Extension = "txt",
            ContentType = "text/plain",
            Size = 1,
            FileContent = new Equibles.Media.Data.Models.FileContent { Bytes = [0x20] },
        };

        var document = new Document
        {
            CommonStock = stock,
            Content = contentFile,
            DocumentType = DocumentType.TenQ,
            ReportingDate = new DateOnly(2025, 5, 1),
            ReportingForDate = new DateOnly(2025, 3, 31),
            AccessionNumber = Accession,
            XbrlStatus = XbrlCaptureStatus.Captured,
            XbrlType = XbrlType.InlineIxbrl,
            XbrlContent = xbrlFile,
            XbrlUncompressedSize = envelope.Length,
        };

        DbContext.Add(document);
        await DbContext.SaveChangesAsync(CancellationToken.None);
        return document;
    }

    // One dimensional fact (iPhone product cut) and one consolidated fact on
    // the same concept/period — only the former may be persisted.
    private static string InlineEnvelope() =>
        "<html xmlns=\"http://www.w3.org/1999/xhtml\" "
        + "xmlns:ix=\"http://www.xbrl.org/2013/inlineXBRL\" "
        + "xmlns:xbrli=\"http://www.xbrl.org/2003/instance\" "
        + "xmlns:xbrldi=\"http://xbrl.org/2006/xbrldi\" "
        + "xmlns:srt=\"http://fasb.org/srt/2024\" "
        + "xmlns:aapl=\"http://www.apple.com/20250329\" "
        + "xmlns:us-gaap=\"http://fasb.org/us-gaap/2024\">"
        + "<body><div style=\"display:none\"><ix:header><ix:resources>"
        + "<xbrli:context id=\"Consolidated\">"
        + "<xbrli:entity><xbrli:identifier scheme=\"cik\">0000320193</xbrli:identifier></xbrli:entity>"
        + "<xbrli:period><xbrli:startDate>2025-01-01</xbrli:startDate><xbrli:endDate>2025-03-31</xbrli:endDate></xbrli:period>"
        + "</xbrli:context>"
        + "<xbrli:context id=\"IPhone\">"
        + "<xbrli:entity><xbrli:identifier scheme=\"cik\">0000320193</xbrli:identifier>"
        + "<xbrli:segment><xbrldi:explicitMember dimension=\"srt:ProductOrServiceAxis\">aapl:IPhoneMember</xbrldi:explicitMember></xbrli:segment>"
        + "</xbrli:entity>"
        + "<xbrli:period><xbrli:startDate>2025-01-01</xbrli:startDate><xbrli:endDate>2025-03-31</xbrli:endDate></xbrli:period>"
        + "</xbrli:context>"
        + "<xbrli:unit id=\"usd\"><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unit>"
        + "</ix:resources></ix:header></div>"
        + "<ix:nonFraction name=\"us-gaap:RevenueFromContractWithCustomerExcludingAssessedTax\" "
        + "contextRef=\"Consolidated\" unitRef=\"usd\" decimals=\"-6\" scale=\"6\">95,359</ix:nonFraction>"
        + "<ix:nonFraction name=\"us-gaap:RevenueFromContractWithCustomerExcludingAssessedTax\" "
        + "contextRef=\"IPhone\" unitRef=\"usd\" decimals=\"-6\" scale=\"6\">46,222</ix:nonFraction>"
        + "</body></html>";
}
