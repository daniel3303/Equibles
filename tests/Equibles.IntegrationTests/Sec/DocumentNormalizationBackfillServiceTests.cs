using System.Text;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Integrations.Sec.Contracts;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Media.Data;
using Equibles.Sec.BusinessLogic;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Sec;

public class DocumentNormalizationBackfillServiceTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly ISecEdgarClient _secEdgarClient = Substitute.For<ISecEdgarClient>();
    private readonly IFileManager _fileManager = Substitute.For<IFileManager>();
    private readonly IDocumentPersistenceService _persistenceService =
        Substitute.For<IDocumentPersistenceService>();
    private readonly EquityIssuer _company;

    public DocumentNormalizationBackfillServiceTests()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableServiceProviderCaching(false)
            .Options;
        _dbContext = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new MediaModuleConfiguration(),
                new SecTestModuleConfiguration(),
            }
        );
        _dbContext.Database.EnsureCreated();

        _company = new EquityIssuer
        {
            Id = Guid.NewGuid(),
            Name = "NVIDIA Corporation",
            Cik = "0001045810",
        };
        _dbContext.Add(_company);
        _dbContext.SaveChanges();
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task Backfill_PendingNvidiaFiling_ReplacesContentForIndexedRechunking()
    {
        var document = SeedDocument(normalizedContentVersion: 0);
        _secEdgarClient
            .GetDocumentContent(
                document.AccessionNumber,
                _company.Cik,
                Arg.Any<CancellationToken>()
            )
            .Returns(NvidiaSubmission);

        var result = await BuildSut().Backfill(batchSize: 10);

        result.Processed.Should().Be(1);
        result.Replaced.Should().Be(1);
        result.Failed.Should().Be(0);
        await _persistenceService
            .Received(1)
            .ReplaceContent(
                Arg.Is<Document>(d =>
                    d.Id == document.Id
                    && d.NormalizedContentVersion == Document.NormalizedContentBuilderVersion
                    && d.NormalizedContentAttempts == 0
                ),
                Arg.Is<byte[]>(bytes => CorrectedTable(Encoding.UTF8.GetString(bytes))),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Backfill_CurrentDocument_DoesNotRefetchOrReplace()
    {
        SeedDocument(Document.NormalizedContentBuilderVersion);

        var result = await BuildSut().Backfill(batchSize: 10);

        result.Processed.Should().Be(0);
        await _secEdgarClient
            .DidNotReceiveWithAnyArgs()
            .GetDocumentContent(default, default, default);
        await _persistenceService
            .DidNotReceiveWithAnyArgs()
            .ReplaceContent(default, default, default);
    }

    [Fact]
    public async Task Backfill_EmptyLegacyAccession_DerivesItFromSourceUrl()
    {
        var document = SeedDocument(normalizedContentVersion: 0);
        document.DocumentType = DocumentType.EightK;
        document.AccessionNumber = "";
        _dbContext.SaveChanges();
        _secEdgarClient
            .GetDocumentContent("0001045810-26-000021", _company.Cik, Arg.Any<CancellationToken>())
            .Returns(NvidiaSubmission);

        var result = await BuildSut()
            .Backfill(batchSize: 1, priorityAccessions: ["0001045810-26-000021"]);

        result.Replaced.Should().Be(1);
        await _secEdgarClient
            .Received(1)
            .GetDocumentContent("0001045810-26-000021", _company.Cik, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Backfill_NonPeriodicDocument_IsOnlySelectedWhenPrioritized()
    {
        var document = SeedDocument(normalizedContentVersion: 0);
        document.DocumentType = DocumentType.EightK;
        _dbContext.SaveChanges();
        _secEdgarClient
            .GetDocumentContent(
                document.AccessionNumber,
                _company.Cik,
                Arg.Any<CancellationToken>()
            )
            .Returns(NvidiaSubmission);

        var stagedResult = await BuildSut().Backfill(batchSize: 1);
        var priorityResult = await BuildSut()
            .Backfill(batchSize: 1, priorityAccessions: [document.AccessionNumber]);

        stagedResult.Processed.Should().Be(0);
        priorityResult.Replaced.Should().Be(1);
    }

    [Fact]
    public async Task Backfill_WhenNormalizedBytesAreUnchanged_ResetsChunksWithoutReplacingFile()
    {
        var document = SeedDocument(normalizedContentVersion: 0);
        _secEdgarClient
            .GetDocumentContent(
                document.AccessionNumber,
                _company.Cik,
                Arg.Any<CancellationToken>()
            )
            .Returns(NvidiaSubmission);
        var normalized = new SecDocumentHtmlToMarkdownConverter().Convert(
            new SecDocumentHtmlNormalizer().Normalize(NvidiaSubmission)
        );
        _fileManager
            .GetContent(Arg.Is<Equibles.Media.Data.Models.File>(f => f.Id == document.ContentId))
            .Returns(Encoding.UTF8.GetBytes(normalized));

        var result = await BuildSut().Backfill(batchSize: 1);

        result.Unchanged.Should().Be(1);
        result.Replaced.Should().Be(0);
        document.NormalizedContentVersion.Should().Be(Document.NormalizedContentBuilderVersion);
        await _persistenceService
            .DidNotReceiveWithAnyArgs()
            .ReplaceContent(default, default, default);
        await _persistenceService
            .Received(1)
            .ResetChunks(Arg.Is<Document>(d => d.Id == document.Id), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Backfill_ShutdownCancellation_StopsTheSecRequestWithoutRecordingFailure()
    {
        var document = SeedDocument(normalizedContentVersion: 0);
        using var cancellation = new CancellationTokenSource();
        _secEdgarClient
            .GetDocumentContent(
                document.AccessionNumber,
                _company.Cik,
                Arg.Any<CancellationToken>()
            )
            .Returns(async call =>
            {
                var token = call.ArgAt<CancellationToken>(2);
                cancellation.Cancel();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return "unreachable";
            });

        var act = () => BuildSut().Backfill(batchSize: 1, cancellationToken: cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        document.NormalizedContentAttempts.Should().Be(1);
        document.NormalizedContentVersion.Should().Be(0);
    }

    [Theory]
    [InlineData("EsefAnnualReport")]
    [InlineData("EsefReport")]
    public async Task Backfill_EsefWithoutCik_ReplaysCapturedEnvelopeAndPreservesOriginal(
        string form
    )
    {
        var document = SeedEsef();
        document.DocumentType = DocumentType.FromValue(form);
        _dbContext.SaveChanges();
        var originalId = document.XbrlContentId;
        var originalBytes = document.XbrlContent.FileContent.Bytes.ToArray();
        _fileManager
            .OpenRead(document.XbrlContent)
            .Returns(_ => new MemoryStream(originalBytes, writable: false));

        var result = await BuildSut().Backfill(batchSize: 10);

        result.Replaced.Should().Be(1);
        result.Failed.Should().Be(0);
        await _persistenceService
            .Received(1)
            .ReplaceContent(
                Arg.Is<Document>(d =>
                    d.Id == document.Id
                    && d.NormalizedContentVersion == Document.EsefEmptyContentRecoveryVersion
                ),
                Arg.Is<byte[]>(b => Encoding.UTF8.GetString(b).Contains("Retained annual report")),
                Arg.Any<CancellationToken>()
            );
        await _secEdgarClient
            .DidNotReceiveWithAnyArgs()
            .GetDocumentContent(default, default, default);
        document.XbrlContentId.Should().Be(originalId);
        document.XbrlContent.FileContent.Bytes.Should().Equal(originalBytes);
        _fileManager.DidNotReceive().DeleteFile(document.XbrlContent);
    }

    [Fact]
    public async Task Backfill_EsefPastTheRetrievalCeiling_ReplacesStaleContentWithAnEmptyBody()
    {
        var document = SeedEsef();
        var filler = new string('a', EsefReportContent.MaxRetrievalHtmlChars + 1);
        var oversized = GzipCompressor.Compress(
            Encoding.UTF8.GetBytes($"<html><body><p>{filler}</p></body></html>")
        );
        _fileManager
            .OpenRead(document.XbrlContent)
            .Returns(_ => new MemoryStream(oversized, writable: false));
        _fileManager.GetContent(document.Content).Returns("Source: Register.\n\n"u8.ToArray());

        var result = await BuildSut().Backfill(batchSize: 10);

        result.Replaced.Should().Be(1);
        result.Failed.Should().Be(0);
        await _persistenceService
            .Received(1)
            .ReplaceContent(
                Arg.Is<Document>(d =>
                    d.Id == document.Id
                    && d.NormalizedContentVersion == Document.EsefEmptyContentRecoveryVersion
                    && d.NormalizedContentAttempts == 0
                ),
                Arg.Is<byte[]>(b => b.Length == 0),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Backfill_EsefWithAnEmptyCapturedEnvelope_FailsAndKeepsItsText()
    {
        var document = SeedEsef();
        var empty = GzipCompressor.Compress([]);
        _fileManager
            .OpenRead(document.XbrlContent)
            .Returns(_ => new MemoryStream(empty, writable: false));

        var result = await BuildSut().Backfill(batchSize: 10);

        result.Failed.Should().Be(1);
        document.NormalizedContentAttempts.Should().Be(1);
        document.NormalizedContentVersion.Should().Be(0);
        await _persistenceService
            .DidNotReceiveWithAnyArgs()
            .ReplaceContent(default, default, default);
    }

    [Theory]
    [InlineData("no-notes", false)]
    [InlineData("malformed", true)]
    [InlineData("wrong-owner", true)]
    [InlineData("missing-period", true)]
    public async Task Backfill_JsonEmptyOrInvalid_PreservesEnvelopeAndTracksOutcome(
        string scenario,
        bool failed
    )
    {
        var document = SeedEsef();
        document.XbrlType = XbrlType.JsonXbrl;
        document.NormalizedContentVersion = 4;
        document.Content.Size = 0;
        _company.LegalEntityIdentifier = "2549001EPXH6NK7I2R78";
        document.ReportingForDate =
            scenario == "missing-period" ? default : new DateOnly(2025, 12, 31);
        var json = """
            {"documentInfo":{"documentType":"https://xbrl.org/2021/xbrl-json",
            "namespaces":{"ifrs-full":"https://xbrl.ifrs.org/taxonomy/2024-03-27/ifrs-full",
            "scheme":"http://standards.iso.org/iso/17442","iso4217":"http://www.xbrl.org/2003/iso4217"},
            "taxonomy":["https://example.test/taxonomy.xsd"]},"facts":{
            "assets":{"value":"100","dimensions":{"concept":"ifrs-full:Assets",
            "entity":"scheme:2549001EPXH6NK7I2R78","period":"2026-01-01T00:00:00","unit":"iso4217:EUR"}}}}
            """;
        if (scenario == "malformed")
            json = "{";
        if (scenario == "wrong-owner")
            json = json.Replace(_company.LegalEntityIdentifier, "529900S21EQ1BO4ESM68");
        var original = GzipCompressor.Compress(Encoding.UTF8.GetBytes(json));
        document.XbrlContent.FileContent.Bytes = original;
        var originalId = document.XbrlContentId;
        _dbContext.SaveChanges();
        _fileManager
            .OpenRead(document.XbrlContent)
            .Returns(_ => new MemoryStream(original, writable: false));
        _fileManager.GetContent(document.Content).Returns(Array.Empty<byte>());

        var result = await BuildSut().Backfill(1);

        result.Processed.Should().Be(1);
        result.Failed.Should().Be(failed ? 1 : 0);
        document
            .NormalizedContentVersion.Should()
            .Be(failed ? 4 : Document.EsefEmptyContentRecoveryVersion);
        document.NormalizedContentAttempts.Should().Be(failed ? 1 : 0);
        document.XbrlContentId.Should().Be(originalId);
        document.XbrlContent.FileContent.Bytes.Should().Equal(original);
        await _persistenceService
            .DidNotReceiveWithAnyArgs()
            .ReplaceContent(default, default, default);
        if (!failed)
        {
            result.Unchanged.Should().Be(1);
            await _persistenceService
                .Received(1)
                .ResetChunks(document, Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Backfill_EdgarFilingWithNoText_FailsAndKeepsItsText()
    {
        var document = SeedDocument(normalizedContentVersion: 0);
        _secEdgarClient
            .GetDocumentContent(
                document.AccessionNumber,
                _company.Cik,
                Arg.Any<CancellationToken>()
            )
            .Returns("   ");

        var result = await BuildSut().Backfill(batchSize: 10);

        result.Failed.Should().Be(1);
        document.NormalizedContentAttempts.Should().Be(1);
        document.NormalizedContentVersion.Should().Be(0);
        await _persistenceService
            .DidNotReceiveWithAnyArgs()
            .ReplaceContent(default, default, default);
    }

    [Theory]
    [InlineData(XbrlType.StandaloneXbrl, XbrlCaptureStatus.Captured)]
    [InlineData(XbrlType.InlineIxbrl, XbrlCaptureStatus.NotChecked)]
    public async Task Backfill_EsefWithoutSupportedCapturedEnvelope_IsNotEligible(
        XbrlType type,
        XbrlCaptureStatus status
    )
    {
        var document = SeedEsef();
        document.XbrlType = type;
        document.XbrlStatus = status;
        await _dbContext.SaveChangesAsync();
        var result = await BuildSut().Backfill(10, includeAllDocumentTypes: true);
        result.Processed.Should().Be(0);
    }

    [Theory]
    [InlineData("EsefAnnualReport", 0, 1, true)]
    [InlineData("EsefReport", 0, 1, true)]
    [InlineData("EsefAnnualReport", 20, 1, false)]
    [InlineData("EsefAnnualReport", 20, 2, false)]
    [InlineData("EsefAnnualReport", 0, 2, true)]
    [InlineData("EsefReport", 0, 2, true)]
    [InlineData("EsefAnnualReport", 0, 3, true)]
    [InlineData("EsefReport", 0, 3, true)]
    [InlineData("EsefAnnualReport", 20, 3, false)]
    [InlineData("EsefAnnualReport", 0, 4, true)]
    [InlineData("EsefReport", 0, 4, true)]
    [InlineData("EsefAnnualReport", 20, 4, false)]
    [InlineData("EsefAnnualReport", 0, 5, false)]
    [InlineData("TenK", 0, 1, false)]
    public void Pending_EmptyEsefRecovery_DoesNotReopenReadableOrCurrentDocuments(
        string form,
        long size,
        int version,
        bool expected
    )
    {
        var document = SeedEsef();
        document.DocumentType = DocumentType.FromValue(form);
        document.NormalizedContentVersion = version;
        document.Content.Size = size;
        _dbContext.SaveChanges();
        new DocumentRepository(_dbContext)
            .GetPendingNormalizedContent()
            .Any(row => row.Id == document.Id)
            .Should()
            .Be(expected);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Backfill_EmptyEsefAtThePriorVersion_UsesRetainedEnvelopeAndAdvancesGeneration(
        int version
    )
    {
        var document = SeedEsef();
        document.NormalizedContentVersion = version;
        document.Content.Size = 0;
        _dbContext.SaveChanges();
        _fileManager
            .OpenRead(document.XbrlContent)
            .Returns(_ => new MemoryStream(
                document.XbrlContent.FileContent.Bytes,
                writable: false
            ));

        var result = await BuildSut().Backfill(10);

        result.Replaced.Should().Be(1);
        result.Failed.Should().Be(0);
        document.NormalizedContentVersion.Should().Be(Document.EsefEmptyContentRecoveryVersion);
        await _secEdgarClient
            .DidNotReceiveWithAnyArgs()
            .GetDocumentContent(default, default, default);
        await _persistenceService
            .Received(1)
            .ReplaceContent(
                Arg.Is<Document>(row => row.Id == document.Id),
                Arg.Is<byte[]>(bytes =>
                    Encoding.UTF8.GetString(bytes).Contains("Retained annual report")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    private Document SeedEsef()
    {
        var document = SeedDocument(0);
        _company.Cik = null;
        document.AccessionNumber = null;
        document.DocumentType = DocumentType.EsefAnnualReport;
        document.SourceUrl = "https://authority.example/report.xhtml";
        document.XbrlType = XbrlType.InlineIxbrl;
        document.XbrlStatus = XbrlCaptureStatus.Captured;
        document.XbrlContent = new Equibles.Media.Data.Models.File
        {
            Name = "retained-report",
            Extension = "gz",
            ContentType = "application/gzip",
            FileContent = new Equibles.Media.Data.Models.FileContent
            {
                Bytes = GzipCompressor.Compress(
                    Encoding.UTF8.GetBytes(
                        "<html xmlns='http://www.w3.org/1999/xhtml'><head><title/></head><body><p>Retained annual report</p></body></html>"
                    )
                ),
            },
        };
        _dbContext.Add(document.XbrlContent);
        _dbContext.SaveChanges();
        return document;
    }

    private DocumentNormalizationBackfillService BuildSut() =>
        new(
            new DocumentRepository(_dbContext),
            _secEdgarClient,
            new SecDocumentHtmlNormalizer(),
            new SecDocumentHtmlToMarkdownConverter(),
            _fileManager,
            _persistenceService,
            Substitute.For<ILogger<DocumentNormalizationBackfillService>>()
        );

    private Document SeedDocument(int normalizedContentVersion)
    {
        var document = new Document
        {
            Id = Guid.NewGuid(),
            EquityIssuerId = _company.Id,
            DocumentType = DocumentType.TenK,
            ReportingDate = new DateOnly(2026, 2, 25),
            ReportingForDate = new DateOnly(2026, 1, 25),
            AccessionNumber = "0001045810-26-000021",
            SourceUrl = "https://www.sec.gov/Archives/edgar/data/1045810/0001045810-26-000021.txt",
            NormalizedContentVersion = normalizedContentVersion,
            Content = new Equibles.Media.Data.Models.File
            {
                Name = "nvda-20260125",
                Size = "old normalized filing"u8.Length,
                Extension = "txt",
                ContentType = "text/plain",
                FileContent = new Equibles.Media.Data.Models.FileContent
                {
                    Bytes = "old normalized filing"u8.ToArray(),
                },
            },
        };
        _dbContext.Add(document);
        _dbContext.SaveChanges();
        return document;
    }

    private static bool CorrectedTable(string markdown) =>
        markdown.Contains(
            "| Compute & Networking | 193,479 | 116,193 | 77,286 | 67 | % |",
            StringComparison.Ordinal
        )
        && markdown.Contains(
            "116,193 | 77,286 | 67 | % |\n| Graphics | 22,459 | 14,304",
            StringComparison.Ordinal
        );

    private const string NvidiaSubmission = """
        <DOCUMENT>
        <TYPE>10-K
        <FILENAME>nvda-20260125.htm
        <TEXT>
        <html><body><table>
        <tr><td></td><td>Jan 25, 2026</td><td></td><td>Jan 26, 2025</td><td></td><td>$ Change</td><td></td><td>% Change</td><td></td></tr>
        <tr><td>Compute &amp; Networking</td><td>$</td><td>193,479</td><td>$</td><td>116,193</td><td>$</td><td>77,286</td><td>67</td><td>%</td></tr>
        <tr><td>Graphics</td><td></td><td>22,459</td><td></td><td>14,304</td><td></td><td>8,155</td><td>57</td><td>%</td></tr>
        <tr><td>Total</td><td>$</td><td>215,938</td><td>$</td><td>130,497</td><td>$</td><td>85,441</td><td>65</td><td>%</td></tr>
        </table></body></html>
        </TEXT>
        </DOCUMENT>
        """;
}
