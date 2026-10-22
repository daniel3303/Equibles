using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.HostedService.Services;
using Equibles.Sec.FinancialFacts.Repositories;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// End-to-end pin of the registrant cover statement: a captured 10-Q/10-K envelope's file number
/// and shell-company flag land as one dated row per filing, carrying the filing's accession,
/// form and date, and only when the unqualified context belongs to a CIK that files for the
/// issuer. Each filing keeps its own row, so a de-SPAC's earlier shell statement survives beside
/// the later operating one and a reader decides by date.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class XbrlFactExtractionServiceRegistrantCoverTests : ParadeDbMcpTestBase
{
    private const string IssuerCik = "0002023676";

    public XbrlFactExtractionServiceRegistrantCoverTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Extract_QuarterlyCover_PersistsTheStatementWithItsFilingProvenance()
    {
        var stock = SeedStock();
        var document = await SeedDocument(
            stock,
            Envelope(IssuerCik, "001-42306", "ixt-sec:boolballotbox", "&#9746;"),
            DocumentType.TenQ,
            "0001213900-26-090049",
            new DateOnly(2026, 8, 14)
        );

        await BuildSut().Extract(document, CancellationToken.None);

        var row = await DbContext.Set<IssuerCoverStatement>().SingleAsync();
        row.EquityIssuerId.Should().Be(stock.Id);
        row.DocumentId.Should().Be(document.Id);
        row.AccessionNumber.Should().Be("0001213900-26-090049");
        row.Form.Should().Be(DocumentType.TenQ);
        row.FiledDate.Should().Be(new DateOnly(2026, 8, 14));
        row.FileNumber.Should().Be("001-42306");
        row.IsShellCompany.Should().BeTrue();
    }

    [Fact]
    public async Task Extract_ShellThenOperatingCovers_KeepsBothDatedStatements()
    {
        var stock = SeedStock();
        var shell = await SeedDocument(
            stock,
            Envelope(IssuerCik, "001-42306", "ixt:fixed-true", "x"),
            DocumentType.TenK,
            "0001213900-26-010001",
            new DateOnly(2026, 3, 2)
        );
        var operating = await SeedDocument(
            stock,
            Envelope(IssuerCik, "001-42306", "ixt:fixed-false", "&#9746;"),
            DocumentType.TenQ,
            "0001213900-26-020002",
            new DateOnly(2026, 8, 14)
        );

        var sut = BuildSut();
        // The historical drain visits newer filings first.
        await sut.Extract(operating, CancellationToken.None);
        await sut.Extract(shell, CancellationToken.None);

        var rows = await DbContext
            .Set<IssuerCoverStatement>()
            .OrderBy(row => row.FiledDate)
            .Select(row => new { row.FiledDate, row.IsShellCompany })
            .ToListAsync();
        rows.Should()
            .Equal(
                new { FiledDate = new DateOnly(2026, 3, 2), IsShellCompany = (bool?)true },
                new { FiledDate = new DateOnly(2026, 8, 14), IsShellCompany = (bool?)false }
            );
    }

    [Fact]
    public async Task Extract_SameFilingTwice_KeepsOneRow()
    {
        var stock = SeedStock();
        var document = await SeedDocument(
            stock,
            Envelope(IssuerCik, "814-00659", "ixt:fixed-false", "x"),
            DocumentType.TenK,
            "0001287032-26-000269",
            new DateOnly(2026, 8, 20)
        );

        var sut = BuildSut();
        await sut.Extract(document, CancellationToken.None);
        await sut.Extract(document, CancellationToken.None);

        (await DbContext.Set<IssuerCoverStatement>().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Extract_AnotherFilersContext_RecordsNothing()
    {
        var stock = SeedStock();
        var document = await SeedDocument(
            stock,
            Envelope("0000320193", "814-00659", "ixt:fixed-true", "x"),
            DocumentType.TenQ,
            "0001213900-26-030003",
            new DateOnly(2026, 8, 14)
        );

        await BuildSut().Extract(document, CancellationToken.None);

        (await DbContext.Set<IssuerCoverStatement>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Extract_CurrentReportCover_RecordsNothing()
    {
        // An 8-K cover carries a file number but no shell-company check box; only periodic
        // reports describe the registrant's structure.
        var stock = SeedStock();
        var document = await SeedDocument(
            stock,
            Envelope(IssuerCik, "001-42306", null, null),
            DocumentType.EightK,
            "0001213900-26-040004",
            new DateOnly(2026, 8, 14)
        );

        await BuildSut().Extract(document, CancellationToken.None);

        (await DbContext.Set<IssuerCoverStatement>().CountAsync()).Should().Be(0);
    }

    private XbrlFactExtractionService BuildSut()
    {
        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(EquiblesFinancialDbContext), DbContext),
            (typeof(FinancialConceptRepository), new FinancialConceptRepository(DbContext)),
            (typeof(EquityIssuerRepository), new EquityIssuerRepository(DbContext)),
            (
                typeof(EquityIssuerTickerEvidenceRepository),
                new EquityIssuerTickerEvidenceRepository(DbContext)
            ),
            (
                typeof(IssuerSecurityRegistrationRepository),
                new IssuerSecurityRegistrationRepository(DbContext)
            ),
            (typeof(IssuerCoverStatementRepository), new IssuerCoverStatementRepository(DbContext))
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

    private EquityIssuer SeedStock()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "LPBB",
            Name: "Launch Two Acquisition Corp.",
            Cik: IssuerCik.TrimStart('0')
        );
        DbContext.Add(stock);
        return stock;
    }

    private async Task<Document> SeedDocument(
        EquityIssuer stock,
        string envelope,
        DocumentType form,
        string accession,
        DateOnly reportingDate
    )
    {
        var compressed = GzipCompressor.Compress(System.Text.Encoding.UTF8.GetBytes(envelope));
        var document = new Document
        {
            EquityIssuerId = stock.Id,
            Content = new File
            {
                Name = "primary-doc",
                Extension = "txt",
                ContentType = "text/plain",
                Size = 1,
                FileContent = new Equibles.Media.Data.Models.FileContent { Bytes = [0x20] },
            },
            DocumentType = form,
            ReportingDate = reportingDate,
            ReportingForDate = reportingDate.AddMonths(-1),
            AccessionNumber = accession,
            XbrlStatus = XbrlCaptureStatus.Captured,
            XbrlType = XbrlType.InlineIxbrl,
            XbrlContent = new File
            {
                Name = "xbrl-envelope",
                Extension = "gz",
                ContentType = "application/gzip",
                Size = compressed.Length,
                FileContent = new Equibles.Media.Data.Models.FileContent { Bytes = compressed },
            },
            XbrlUncompressedSize = envelope.Length,
        };

        DbContext.Add(document);
        await DbContext.SaveChangesAsync(CancellationToken.None);
        return document;
    }

    // A minimal inline envelope stating only the registrant's cover facts in an unqualified
    // context of the given CIK.
    private static string Envelope(string cik, string fileNumber, string shellFormat, string shell)
    {
        var shellFact =
            shell == null
                ? ""
                : $"<ix:nonNumeric contextRef=\"c-1\" name=\"dei:EntityShellCompany\" format=\"{shellFormat}\">{shell}</ix:nonNumeric>";
        return "<html xmlns=\"http://www.w3.org/1999/xhtml\" "
            + "xmlns:ix=\"http://www.xbrl.org/2013/inlineXBRL\" "
            + "xmlns:ixt=\"http://www.xbrl.org/inlineXBRL/transformation/2020-02-12\" "
            + "xmlns:ixt-sec=\"http://www.sec.gov/inlineXBRL/transformation/2015-08-31\" "
            + "xmlns:xbrli=\"http://www.xbrl.org/2003/instance\" "
            + "xmlns:dei=\"http://xbrl.sec.gov/dei/2026\">"
            + "<body><ix:header><ix:resources>"
            + "<xbrli:context id=\"c-1\"><xbrli:entity><xbrli:identifier scheme=\"http://www.sec.gov/CIK\">"
            + cik
            + "</xbrli:identifier></xbrli:entity><xbrli:period><xbrli:startDate>2026-04-01</xbrli:startDate>"
            + "<xbrli:endDate>2026-06-30</xbrli:endDate></xbrli:period></xbrli:context>"
            + "</ix:resources></ix:header>"
            + $"<ix:nonNumeric contextRef=\"c-1\" name=\"dei:EntityFileNumber\">{fileNumber}</ix:nonNumeric>"
            + shellFact
            + "</body></html>";
    }
}
