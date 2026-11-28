using System.Security.Cryptography;
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
    private readonly EsefVisualReportTestHandler _visualReports = new();
    private readonly IDocumentPersistenceService _persistenceService =
        Substitute.For<IDocumentPersistenceService>();
    private readonly EquityIssuer _company;

    public DocumentNormalizationBackfillServiceTests()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Backfill_StampsTheHeaderAcceptanceTimeOnlyWhereNoneIsStored(
        bool alreadyStamped
    )
    {
        var document = SeedDocument(normalizedContentVersion: 0);
        var stored = new DateTime(2026, 2, 25, 21, 0, 0, DateTimeKind.Utc);
        if (alreadyStamped)
        {
            document.AcceptedAt = stored;
            _dbContext.SaveChanges();
        }
        _secEdgarClient
            .GetDocumentContent(
                document.AccessionNumber,
                _company.Cik,
                Arg.Any<CancellationToken>()
            )
            .Returns(AcceptanceHeader + NvidiaSubmission);

        await BuildSut().Backfill(batchSize: 1);

        // 16:12:34 Eastern standard time is 21:12:34 UTC.
        var expected = alreadyStamped
            ? stored
            : new DateTime(2026, 2, 25, 21, 12, 34, DateTimeKind.Utc);
        await _persistenceService
            .Received(1)
            .ReplaceContent(
                Arg.Is<Document>(d => d.Id == document.Id && d.AcceptedAt == expected),
                Arg.Any<byte[]>(),
                Arg.Any<CancellationToken>()
            );
    }

    private const string AcceptanceHeader =
        "<SEC-HEADER>0001045810-26-000021.hdr.sgml : 20260225\n<ACCEPTANCE-DATETIME>20260225161234\n</SEC-HEADER>\n";

    // The unchanged path saves through the repository rather than the persistence service, so the stamp
    // must reach the stored row there too.
    [Fact]
    public async Task Backfill_RebuiltTextThatStaysIdentical_StoresTheAcceptanceTime()
    {
        var document = SeedDocument(normalizedContentVersion: Document.FirstContentBuilderVersion);
        var submission = AcceptanceHeader + NvidiaSubmission;
        _secEdgarClient
            .GetDocumentContent(
                document.AccessionNumber,
                _company.Cik,
                Arg.Any<CancellationToken>()
            )
            .Returns(submission);
        var normalized = new SecDocumentHtmlToMarkdownConverter().Convert(
            new SecDocumentHtmlNormalizer().Normalize(submission)
        );
        _fileManager
            .GetContent(Arg.Is<Equibles.Media.Data.Models.File>(f => f.Id == document.ContentId))
            .Returns(Encoding.UTF8.GetBytes(normalized));

        var result = await BuildSut().Backfill(batchSize: 1);

        result.Unchanged.Should().Be(1);
        _dbContext.ChangeTracker.Clear();
        (await _dbContext.Set<Document>().SingleAsync(d => d.Id == document.Id))
            .AcceptedAt.Should()
            .Be(new DateTime(2026, 2, 25, 21, 12, 34, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Backfill_FailedRebuild_StoresNoAcceptanceTime()
    {
        var document = SeedDocument(normalizedContentVersion: 0);
        _secEdgarClient
            .GetDocumentContent(
                document.AccessionNumber,
                _company.Cik,
                Arg.Any<CancellationToken>()
            )
            .Returns(AcceptanceHeader + "   ");

        var result = await BuildSut().Backfill(batchSize: 1);

        result.Failed.Should().Be(1);
        _dbContext.ChangeTracker.Clear();
        (await _dbContext.Set<Document>().SingleAsync(d => d.Id == document.Id))
            .AcceptedAt.Should()
            .BeNull();
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

    // A holding-company reorganization gave the issuer a new CIK; EDGAR still serves the 2023
    // filing only under the CIK that filed it, which its stored address names.
    [Fact]
    public async Task Backfill_FilingStoredUnderAnEarlierCik_IsFetchedUnderTheCikOfItsAddress()
    {
        var document = SeedDocument(normalizedContentVersion: 0);
        document.AccessionNumber = "0001654954-23-004613";
        document.SourceUrl =
            "https://www.sec.gov/Archives/edgar/data/0001593538/0001654954-23-004613.txt";
        _company.Cik = "2152623";
        _dbContext.SaveChanges();
        _secEdgarClient
            .GetDocumentContent("0001654954-23-004613", "1593538", Arg.Any<CancellationToken>())
            .Returns(NvidiaSubmission);
        _secEdgarClient
            .GetDocumentContent("0001654954-23-004613", "2152623", Arg.Any<CancellationToken>())
            .Returns<Task<string>>(_ => throw new HttpRequestException("404 (Not Found)"));

        var result = await BuildSut().Backfill(batchSize: 1);

        result.Replaced.Should().Be(1);
        result.Failed.Should().Be(0);
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
    public async Task Backfill_RebuiltTextThatStaysIdentical_KeepsItsChunks()
    {
        // A normalizer change re-reads every 10-K; one it leaves byte-identical stays searchable.
        var document = SeedDocument(normalizedContentVersion: Document.FirstContentBuilderVersion);
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
        await _persistenceService.DidNotReceiveWithAnyArgs().ResetChunks(default, default);
        _dbContext.ChangeTracker.Clear();
        (await _dbContext.Set<Document>().SingleAsync(d => d.Id == document.Id))
            .NormalizedContentVersion.Should()
            .Be(Document.NormalizedContentBuilderVersion);
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
                    && d.NormalizedContentVersion
                        == Document.ContentBuilderVersion(DocumentType.EsefAnnualReport)
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
                    && d.NormalizedContentVersion
                        == Document.ContentBuilderVersion(DocumentType.EsefAnnualReport)
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

    // Reports before 2022 tag no notes, so a JSON report without them takes the text of the visual report
    // the index states beside its JSON address.
    [Fact]
    public async Task Backfill_JsonWithoutNotes_TakesItsVisualReportText()
    {
        var document = SeedJsonWithoutNotes("2549001EPXH6NK7I2R78");
        _visualReports.StateFiling(
            "2549001EPXH6NK7I2R78",
            "2025-12-31",
            "/2549001EPXH6NK7I2R78/2025-12-31/ESEF/BE/0/report.json",
            "/2549001EPXH6NK7I2R78/2025-12-31/ESEF/BE/0/report/reports/report.xhtml",
            EsefVisualReportTestHandler.Report(
                "2549001EPXH6NK7I2R78",
                "2025-12-31",
                "Margins widened in 2025."
            )
        );
        byte[] stored = null;
        await _persistenceService.ReplaceContent(
            document,
            Arg.Do<byte[]>(content => stored = content),
            Arg.Any<CancellationToken>()
        );

        var result = await BuildSut().Backfill(1);

        result.Replaced.Should().Be(1);
        Encoding.UTF8.GetString(stored).Should().Contain("Margins widened in 2025.");
        document
            .NormalizedContentVersion.Should()
            .Be(Document.ContentBuilderVersion(DocumentType.EsefAnnualReport));
    }

    // Each visual report is a large download over the server's one link, so a batch takes only a few.
    [Fact]
    public async Task Backfill_ManyJsonReportsWithoutNotes_TakesOnlyTheVisualReportsABatchAllows()
    {
        for (
            var index = 0;
            index < DocumentNormalizationBackfillService.MaxVisualReportsPerBatch + 2;
            index++
        )
            SeedJsonWithoutNotes("2549001EPXH6NK7I2R78");

        var result = await BuildSut().Backfill(16);

        result.Processed.Should().Be(DocumentNormalizationBackfillService.MaxVisualReportsPerBatch);
    }

    private Document SeedJsonWithoutNotes(string lei)
    {
        var document = SeedEsef();
        document.XbrlType = XbrlType.JsonXbrl;
        document.NormalizedContentVersion = 8;
        document.Content.Size = 0;
        document.SourceUrl = $"https://filings.xbrl.org/{lei}/2025-12-31/ESEF/BE/0/report.json";
        document.ReportingForDate = new DateOnly(2025, 12, 31);
        _company.LegalEntityIdentifier = lei;
        var json =
            "{\"documentInfo\":{\"documentType\":\"https://xbrl.org/2021/xbrl-json\",\"namespaces\":{"
            + "\"ifrs-full\":\"https://xbrl.ifrs.org/taxonomy/2024-03-27/ifrs-full\","
            + "\"scheme\":\"http://standards.iso.org/iso/17442\",\"iso4217\":\"http://www.xbrl.org/2003/iso4217\"},"
            + "\"taxonomy\":[\"https://example.test/taxonomy.xsd\"]},\"facts\":{\"assets\":{\"value\":\"100\","
            + $"\"dimensions\":{{\"concept\":\"ifrs-full:Assets\",\"entity\":\"scheme:{lei}\","
            + "\"period\":\"2026-01-01T00:00:00\",\"unit\":\"iso4217:EUR\"}}}}";
        var original = GzipCompressor.Compress(Encoding.UTF8.GetBytes(json));
        document.XbrlContent.FileContent.Bytes = original;
        _dbContext.SaveChanges();
        _fileManager
            .OpenRead(document.XbrlContent)
            .Returns(_ => new MemoryStream(original, writable: false));
        _fileManager.GetContent(document.Content).Returns(Array.Empty<byte>());
        return document;
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
            .Be(failed ? 4 : Document.ContentBuilderVersion(DocumentType.EsefAnnualReport));
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
    [InlineData("EsefAnnualReport", 20, 1, true)]
    [InlineData("EsefAnnualReport", 20, 2, true)]
    [InlineData("EsefAnnualReport", 0, 2, true)]
    [InlineData("EsefReport", 0, 2, true)]
    [InlineData("EsefAnnualReport", 0, 3, true)]
    [InlineData("EsefReport", 0, 3, true)]
    [InlineData("EsefAnnualReport", 20, 3, true)]
    [InlineData("EsefAnnualReport", 0, 4, true)]
    [InlineData("EsefReport", 0, 4, true)]
    [InlineData("EsefAnnualReport", 20, 4, true)]
    [InlineData("EsefAnnualReport", 0, 5, true)]
    [InlineData("EsefAnnualReport", 20, 5, true)]
    // An empty report below generation 9 is built again: its text is now written past the conversion
    // limit, a byte-order mark or not, and a JSON one reads its visual report. A readable one stays settled.
    [InlineData("EsefAnnualReport", 0, 6, true)]
    [InlineData("EsefAnnualReport", 0, 8, true)]
    [InlineData("EsefAnnualReport", 0, 9, false)]
    [InlineData("EsefAnnualReport", 20, 6, false)]
    [InlineData("EsefAnnualReport", 20, 8, false)]
    [InlineData("EsefReport", 20, 6, false)]
    [InlineData("TenK", 0, 1, false)]
    public void Pending_InlineEsefReplay_ReopensStaleTextButKeepsCurrentDocumentsSettled(
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
        document
            .NormalizedContentVersion.Should()
            .Be(Document.ContentBuilderVersion(DocumentType.EsefAnnualReport));
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

    [Theory]
    [InlineData(0, true)]
    [InlineData(Document.NormalizedContentBuilderVersion - 1, true)]
    [InlineData(Document.NormalizedContentBuilderVersion, false)]
    public void Pending_EdgarText_FollowsTheEdgarBuilderVersion(int version, bool expected)
    {
        var edgar = SeedDocument(version);

        new DocumentRepository(_dbContext)
            .GetPendingNormalizedContent()
            .Any(row => row.Id == edgar.Id)
            .Should()
            .Be(expected);
    }

    [Fact]
    public void Pending_ReadableEsefBelowTheEdgarVersion_StaysSettled()
    {
        // The EDGAR builder version moves alone; ESEF text follows its own versions.
        var esef = SeedEsef();
        esef.XbrlType = XbrlType.JsonXbrl;
        esef.NormalizedContentVersion = Document.NormalizedContentBuilderVersion - 1;
        esef.Content.Size = 20;
        _dbContext.SaveChanges();

        new DocumentRepository(_dbContext)
            .GetPendingNormalizedContent()
            .Any(row => row.Id == esef.Id)
            .Should()
            .BeFalse();
    }

    [Theory]
    [InlineData(1, 20, false)]
    [InlineData(4, 20, false)]
    [InlineData(4, 0, true)]
    [InlineData(5, 0, true)]
    [InlineData(6, 0, true)]
    [InlineData(8, 0, true)]
    [InlineData(9, 0, false)]
    [InlineData(6, 20, false)]
    [InlineData(8, 20, false)]
    public void Pending_JsonRecovery_RemainsLimitedToPriorEmptyReports(
        int version,
        long size,
        bool expected
    )
    {
        var document = SeedEsef();
        document.XbrlType = XbrlType.JsonXbrl;
        document.NormalizedContentVersion = version;
        document.Content.Size = size;
        _dbContext.SaveChanges();
        new DocumentRepository(_dbContext)
            .GetPendingNormalizedContent()
            .Any(row => row.Id == document.Id)
            .Should()
            .Be(expected);
    }

    [Fact]
    public async Task Backfill_ReadableEsefWithUnchangedText_PreservesChunksAndSettlesReplay()
    {
        var document = SeedEsef();
        document.NormalizedContentVersion = 1;
        document.ChunkedAt = DateTime.UtcNow;
        document.ChunkAttempts = 2;
        var chunkedAt = document.ChunkedAt;
        _dbContext.SaveChanges();
        _fileManager
            .OpenRead(document.XbrlContent)
            .Returns(_ => new MemoryStream(
                document.XbrlContent.FileContent.Bytes,
                writable: false
            ));
        _fileManager.GetContent(document.Content).Returns("Retained annual report"u8.ToArray());

        var result = await BuildSut().Backfill(1);

        result.Unchanged.Should().Be(1);
        result.Failed.Should().Be(0);
        _dbContext.ChangeTracker.Clear();
        var saved = _dbContext.Set<Document>().Single(row => row.Id == document.Id);
        saved
            .NormalizedContentVersion.Should()
            .Be(Document.ContentBuilderVersion(DocumentType.EsefAnnualReport));
        saved.NormalizedContentAttempts.Should().Be(0);
        saved.ChunkedAt.Should().Be(chunkedAt);
        saved.ChunkAttempts.Should().Be(2);
        await _persistenceService.DidNotReceiveWithAnyArgs().ResetChunks(default, default);
        await _persistenceService
            .DidNotReceiveWithAnyArgs()
            .ReplaceContent(default, default, default);
        (await BuildSut().Backfill(1)).Processed.Should().Be(0);
    }

    [Fact]
    public async Task Backfill_ReadableEsefThatNowExceedsTheCeiling_KeepsExistingTextAndRecordsFailure()
    {
        var document = SeedEsef();
        document.NormalizedContentVersion = 1;
        var oldContentId = document.ContentId;
        _dbContext.SaveChanges();
        var oversized = GzipCompressor.Compress(
            Encoding.UTF8.GetBytes(
                "<html><body><p>"
                    + new string('a', EsefReportContent.MaxRetrievalHtmlChars + 1)
                    + "</p></body></html>"
            )
        );
        _fileManager
            .OpenRead(document.XbrlContent)
            .Returns(_ => new MemoryStream(oversized, writable: false));

        var result = await BuildSut().Backfill(1);

        result.Failed.Should().Be(1);
        _dbContext.ChangeTracker.Clear();
        var saved = _dbContext.Set<Document>().Single(row => row.Id == document.Id);
        saved.ContentId.Should().Be(oldContentId);
        saved.NormalizedContentVersion.Should().Be(1);
        document.NormalizedContentAttempts.Should().Be(1);
        await _persistenceService
            .DidNotReceiveWithAnyArgs()
            .ReplaceContent(default, default, default);
    }

    [Fact]
    public async Task Backfill_ReadableGlastonEnvelope_RejoinsItsCompleteBorrowingDisclosure()
    {
        var document = SeedEsef();
        document.NormalizedContentVersion = 1;
        var original = System.IO.File.ReadAllBytes(
            Path.Combine(
                AppContext.BaseDirectory,
                "TestAssets/Esef/glaston-2025-legacy-envelope.xhtml.gz"
            )
        );
        Convert
            .ToHexStringLower(SHA256.HashData(original))
            .Should()
            .Be("4cf6823ed339e46502210ec80a7a452d464d332fa623c4be2af837bd6f0469f8");
        document.XbrlContent.FileContent.Bytes = original;
        var originalId = document.XbrlContentId;
        _dbContext.SaveChanges();
        _fileManager
            .OpenRead(document.XbrlContent)
            .Returns(_ => new MemoryStream(original, writable: false));

        var result = await BuildSut().Backfill(1);

        result.Replaced.Should().Be(1);
        result.Failed.Should().Be(0);
        await _persistenceService
            .Received(1)
            .ReplaceContent(
                document,
                Arg.Is<byte[]>(bytes =>
                    bytes.Length == 584638
                    && Convert.ToHexStringLower(SHA256.HashData(bytes))
                        == "3adbf26bc784e81e71bf82adab8dc21544219ef480c613e9ca141d6189bff2f2"
                ),
                Arg.Any<CancellationToken>()
            );
        document.XbrlContentId.Should().Be(originalId);
        document.XbrlContent.FileContent.Bytes.Should().Equal(original);
        await _secEdgarClient
            .DidNotReceiveWithAnyArgs()
            .GetDocumentContent(default, default, default);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("wrong-hash")]
    [InlineData("wrong-size")]
    [InlineData("wrong-expanded-size")]
    [InlineData("bad-header")]
    [InlineData("wrong-issuer")]
    [InlineData("wrong-period")]
    [InlineData("future-period")]
    [InlineData("storage-wrapper")]
    [InlineData("oversized-source")]
    [InlineData("oversized-inflated")]
    public async Task Backfill_RetainedAnnualOriginal_IsPreferredOnlyWithCompleteCaptureAndIdentityProof(
        string scenario
    )
    {
        var document = SeedEsef();
        document.NormalizedContentVersion = 1;
        _company.LegalEntityIdentifier = "969500N6BAT2PU986341";
        document.ReportingForDate = new DateOnly(2026, 4, 30);
        document.ReportingDate = new DateOnly(2026, 8, 31);
        var original = System.IO.File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "TestAssets/Esef/dila-tff-2026.xhtml")
        );
        var compressed = GzipCompressor.Compress(original);
        if (scenario == "bad-header")
            compressed[0] = 0;
        var file = new Equibles.Media.Data.Models.File
        {
            Name = "original",
            Extension = "gz",
            ContentType = "application/gzip",
            Size = compressed.Length,
            ContentHash = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(compressed)),
            FileContent = new Equibles.Media.Data.Models.FileContent { Bytes = compressed },
        };
        document.AsFiledHtmlContent = file;
        document.AsFiledHtmlUncompressedSize = original.Length;
        if (scenario == "wrong-hash")
            file.ContentHash = "sha256:" + new string('0', 64);
        if (scenario == "wrong-size")
            file.Size++;
        if (scenario == "wrong-expanded-size")
            document.AsFiledHtmlUncompressedSize++;
        if (scenario == "wrong-issuer")
            _company.LegalEntityIdentifier = "2549001EPXH6NK7I2R78";
        if (scenario == "wrong-period")
            document.ReportingForDate = new DateOnly(2025, 4, 30);
        if (scenario == "future-period")
            document.ReportingDate = new DateOnly(2026, 4, 29);
        if (scenario == "storage-wrapper")
            file.StorageProvider = Equibles.Media.Data.Models.StorageProvider.FileSystemGzip;
        if (scenario == "oversized-source")
            file.Size = EsefReportEnvelope.MaximumSourceBytes + 1L;
        if (scenario == "oversized-inflated")
            document.AsFiledHtmlUncompressedSize = EsefReportEnvelope.MaximumSourceBytes + 1L;
        _dbContext.Add(file);
        _dbContext.SaveChanges();
        var originalId = file.Id;
        var envelopeId = document.XbrlContentId;
        _fileManager.OpenRead(file).Returns(_ => new MemoryStream(compressed, writable: false));

        var result = await BuildSut().Backfill(1);

        result.Processed.Should().Be(1);
        result.Failed.Should().Be(scenario == "valid" ? 0 : 1);
        result.Replaced.Should().Be(scenario == "valid" ? 1 : 0);
        document.AsFiledHtmlContentId.Should().Be(originalId);
        document.XbrlContentId.Should().Be(envelopeId);
        file.FileContent.Bytes.Should().Equal(compressed);
        await _fileManager.DidNotReceive().OpenRead(document.XbrlContent);
        await _secEdgarClient
            .DidNotReceiveWithAnyArgs()
            .GetDocumentContent(default, default, default);
        if (scenario == "valid")
            await _persistenceService
                .Received(1)
                .ReplaceContent(
                    document,
                    Arg.Is<byte[]>(bytes =>
                        bytes.Length > 0
                        && !Encoding.UTF8.GetString(bytes).Contains("Retained annual report")
                    ),
                    Arg.Any<CancellationToken>()
                );
        else
            await _persistenceService
                .DidNotReceiveWithAnyArgs()
                .ReplaceContent(default, default, default);
    }

    // An original whose envelope is past the limit cannot be proven again, so the envelope proven
    // and stored at import is read instead, and the report reaches the current generation.
    [Fact]
    public async Task Backfill_RetainedOriginalPastTheEnvelopeLimit_ReadsTheEnvelopeStoredAtImport()
    {
        var document = SeedEsef();
        document.NormalizedContentVersion = 1;
        var prose = new StringBuilder("<html xmlns=\"http://www.w3.org/1999/xhtml\"><body>");
        while (prose.Length <= EsefReportEnvelope.MaximumEnvelopeBytes)
            prose.Append("<p class=\"").Append('a', 200).Append("\">Unproven original</p>");
        var original = Encoding.UTF8.GetBytes(prose.Append("</body></html>").ToString());
        var compressed = GzipCompressor.Compress(original);
        var file = new Equibles.Media.Data.Models.File
        {
            Name = "original",
            Extension = "gz",
            ContentType = "application/gzip",
            Size = compressed.Length,
            ContentHash = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(compressed)),
            FileContent = new Equibles.Media.Data.Models.FileContent { Bytes = compressed },
        };
        document.AsFiledHtmlContent = file;
        document.AsFiledHtmlUncompressedSize = original.Length;
        _dbContext.Add(file);
        _dbContext.SaveChanges();
        _fileManager.OpenRead(file).Returns(_ => new MemoryStream(compressed, writable: false));
        _fileManager
            .OpenRead(document.XbrlContent)
            .Returns(_ => new MemoryStream(
                document.XbrlContent.FileContent.Bytes,
                writable: false
            ));

        var result = await BuildSut().Backfill(1);

        result.Failed.Should().Be(0);
        result.Replaced.Should().Be(1);
        document
            .NormalizedContentVersion.Should()
            .Be(Document.ContentBuilderVersion(DocumentType.EsefAnnualReport));
        await _persistenceService
            .Received(1)
            .ReplaceContent(
                document,
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
            _visualReports.Service(),
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
            "| Compute & Networking | 193,479 | 116,193 | 77,286 | 67% |",
            StringComparison.Ordinal
        )
        && markdown.Contains(
            "116,193 | 77,286 | 67% |\n| Graphics | 22,459 | 14,304",
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
