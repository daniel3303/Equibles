using System.IO.Compression;
using System.Text;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Integrations.Sec.Contracts;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Media.Data;
using Equibles.Media.Repositories;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Repositories;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Sec;

// A 40-F captured before the parser read whole inline document sets kept only its cover. The
// recapture re-reads it once from EDGAR, replaces a capture that grew, and queues re-extraction.
public class XbrlDocumentSetRecaptureServiceTests : IDisposable
{
    private const string Cover =
        "<html xmlns:ix=\"http://www.xbrl.org/2013/inlineXBRL\"><body><ix:nonNumeric name=\"dei:TradingSymbol\">GIB</ix:nonNumeric></body></html>";
    private const string Statements =
        "<html xmlns:ix=\"http://www.xbrl.org/2013/inlineXBRL\"><body><ix:nonFraction name=\"ifrs-full:Revenue\">1</ix:nonFraction></body></html>";
    private const string Accession = "0001193125-25-322911";

    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly ISecEdgarClient _secEdgarClient = Substitute.For<ISecEdgarClient>();
    private readonly FileManager _fileManager;
    private readonly XbrlDocumentSetRecaptureService _service;
    private readonly EquityIssuer _issuer;

    public XbrlDocumentSetRecaptureServiceTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new MediaModuleConfiguration(),
            new SecTestModuleConfiguration(),
            new FinancialFactsModuleConfiguration()
        );
        _fileManager = FileManagerTestFactory.Create(new FileRepository(_dbContext));
        _service = new XbrlDocumentSetRecaptureService(
            new DocumentRepository(_dbContext),
            new FinancialFactRepository(_dbContext),
            _secEdgarClient,
            _fileManager,
            Substitute.For<ILogger<XbrlDocumentSetRecaptureService>>()
        );
        _issuer = new EquityIssuer
        {
            Id = Guid.NewGuid(),
            Name = "Document Set Issuer",
            Cik = "0001061574",
        };
        _dbContext.Add(_issuer);
        _dbContext.SaveChanges();
    }

    public void Dispose() => _dbContext.Dispose();

    private static string Submission(params string[] bodies) =>
        "<SEC-DOCUMENT>\n"
        + string.Concat(
            bodies.Select(
                (body, index) =>
                    $"<DOCUMENT>\n<TYPE>{(index == 0 ? "40-F" : "EX-99.2")}\n<SEQUENCE>{index + 1}\n<FILENAME>{(index == 0 ? "gib-40f.htm" : "gib-ex992.htm")}\n<TEXT>\n{body}\n</TEXT>\n</DOCUMENT>\n"
            )
        )
        + "</SEC-DOCUMENT>";

    private async Task<Document> Seed(
        DocumentType type,
        string factUnit = "shares",
        DateTime? capturedAt = null,
        int attempts = 0,
        string accession = Accession
    )
    {
        var file = await _fileManager.SaveInternalFile(
            GzipCompressor.Compress(Encoding.UTF8.GetBytes(Cover)),
            "gib-40f.htm",
            "gz",
            "application/gzip"
        );
        file.CreationTime = capturedAt ?? new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var document = new Document
        {
            Id = Guid.NewGuid(),
            EquityIssuerId = _issuer.Id,
            DocumentType = type,
            ReportingDate = new DateOnly(2025, 12, 17),
            AccessionNumber = accession,
            SourceUrl = $"https://www.sec.gov/Archives/edgar/data/1061574/{Accession}.txt",
            XbrlStatus = XbrlCaptureStatus.Captured,
            XbrlType = XbrlType.InlineIxbrl,
            XbrlContent = file,
            XbrlUncompressedSize = Encoding.UTF8.GetByteCount(Cover),
            XbrlFactsVersion = 8,
            XbrlCaptureAttempts = attempts,
        };
        _dbContext.Add(document);
        _dbContext.Add(
            new FinancialFact
            {
                EquityIssuerId = _issuer.Id,
                FinancialConceptId = Guid.NewGuid(),
                DocumentId = document.Id,
                Unit = factUnit,
                PeriodStart = new DateOnly(2025, 9, 30),
                PeriodEnd = new DateOnly(2025, 9, 30),
                Form = type,
                FiledDate = document.ReportingDate,
                AccessionNumber = Accession,
            }
        );
        await _dbContext.SaveChangesAsync();
        return document;
    }

    private async Task<string> CapturedText(Document document)
    {
        var stored = await _fileManager.GetContent(document.XbrlContent);
        await using var gzip = new GZipStream(new MemoryStream(stored), CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    [Fact]
    public async Task Pending_SelectsOnlyPreFixPeriodicCapturesWithoutACurrencyFact()
    {
        var coverOnly = await Seed(DocumentType.FortyF);
        var sixK = await Seed(DocumentType.SixKa);
        var perShareOnly = await Seed(DocumentType.FortyF, factUnit: "USD/shares");
        await Seed(DocumentType.FortyF, factUnit: "CAD");
        await Seed(DocumentType.FortyF, capturedAt: XbrlDocumentSetRecaptureService.Cutoff);
        await Seed(DocumentType.EightK);
        await Seed(DocumentType.FortyF, attempts: Document.MaxXbrlCaptureAttempts);

        var pending = await _service.Pending().Select(d => d.Id).ToListAsync();

        pending.Should().BeEquivalentTo([coverOnly.Id, sixK.Id, perShareOnly.Id]);
    }

    [Fact]
    public async Task Recapture_DocumentSet_ReplacesTheCaptureAndQueuesReExtraction()
    {
        var document = await Seed(DocumentType.FortyF);
        var previousFileId = document.XbrlContentId;
        _secEdgarClient
            .GetDocumentContent(Accession, _issuer.Cik, Arg.Any<CancellationToken>())
            .Returns(Submission(Cover, Statements));

        var result = await _service.Recapture(batchSize: 10);

        result.Grown.Should().Be(1);
        document.XbrlContentId.Should().NotBe(previousFileId!.Value);
        (await CapturedText(document)).Should().Be(Cover + "\n" + Statements);
        document.XbrlUncompressedSize.Should().Be(Encoding.UTF8.GetByteCount(Cover + "\n" + Statements));
        document.XbrlFactsVersion.Should().Be(0);
        document.XbrlFactsAttempts.Should().Be(0);
        document.XbrlCaptureAttempts.Should().Be(Document.MaxXbrlCaptureAttempts);
        (await _dbContext.Set<File>().AnyAsync(f => f.Id == previousFileId)).Should().BeFalse();
        (await _service.Pending().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Recapture_SingleDocument_KeepsTheCaptureAndRetiresTheDocument()
    {
        var document = await Seed(DocumentType.FortyF);
        var previousFileId = document.XbrlContentId;
        _secEdgarClient
            .GetDocumentContent(Accession, _issuer.Cik, Arg.Any<CancellationToken>())
            .Returns(Submission(Cover));

        var result = await _service.Recapture(batchSize: 10);

        result.Unchanged.Should().Be(1);
        document.XbrlContentId.Should().Be(previousFileId!.Value);
        document.XbrlFactsVersion.Should().Be(8);
        (await _service.Pending().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Recapture_FetchFailure_CountsAnAttemptAndStaysPending()
    {
        var document = await Seed(DocumentType.FortyF);
        _secEdgarClient
            .GetDocumentContent(Accession, _issuer.Cik, Arg.Any<CancellationToken>())
            .Returns<Task<string>>(_ => throw new HttpRequestException("EDGAR unavailable"));

        var result = await _service.Recapture(batchSize: 10);

        result.Failed.Should().Be(1);
        var stored = await _dbContext.Set<Document>().AsNoTracking().SingleAsync(d => d.Id == document.Id);
        stored.XbrlCaptureAttempts.Should().Be(1);
        stored.XbrlFactsVersion.Should().Be(8);
        (await _service.Pending().Select(d => d.Id).ToListAsync()).Should().Equal(document.Id);
    }

    // A store failure after a successful fetch is one failed attempt, never a finished re-read.
    [Fact]
    public async Task Recapture_StorageFailure_CountsAnAttemptAndStaysPending()
    {
        var document = await Seed(DocumentType.FortyF);
        var failingStore = Substitute.For<IFileManager>();
        failingStore
            .SaveInternalFile(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns<Task<File>>(_ => throw new IOException("disk full"));
        var service = new XbrlDocumentSetRecaptureService(
            new DocumentRepository(_dbContext),
            new FinancialFactRepository(_dbContext),
            _secEdgarClient,
            failingStore,
            Substitute.For<ILogger<XbrlDocumentSetRecaptureService>>()
        );
        _secEdgarClient
            .GetDocumentContent(Accession, _issuer.Cik, Arg.Any<CancellationToken>())
            .Returns(Submission(Cover, Statements));

        var result = await service.Recapture(batchSize: 10);

        result.Failed.Should().Be(1);
        var stored = await _dbContext.Set<Document>().AsNoTracking().SingleAsync(d => d.Id == document.Id);
        stored.XbrlCaptureAttempts.Should().Be(1);
        stored.XbrlContentId.Should().Be(document.XbrlContentId);
        (await service.Pending().Select(d => d.Id).ToListAsync()).Should().Equal(document.Id);
    }

    [Fact]
    public async Task Recapture_NoStoredAccession_ReadsTheSourceUrlAccessionAndKeepsIt()
    {
        var document = await Seed(DocumentType.FortyF, accession: null);
        _secEdgarClient
            .GetDocumentContent(Accession, _issuer.Cik, Arg.Any<CancellationToken>())
            .Returns(Submission(Cover, Statements));

        var result = await _service.Recapture(batchSize: 10);

        result.Grown.Should().Be(1);
        document.AccessionNumber.Should().Be(Accession);
    }
}
