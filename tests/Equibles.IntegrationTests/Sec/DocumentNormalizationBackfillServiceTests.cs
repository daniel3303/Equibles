using System.Text;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Integrations.Sec.Contracts;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Media.Data;
using Equibles.Sec.BusinessLogic;
using Equibles.Sec.BusinessLogic.Processing;
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
    private readonly IDocumentProcessor _documentProcessor = Substitute.For<IDocumentProcessor>();
    private readonly CommonStock _company;

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

        _company = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "NVDA",
            Name = "NVIDIA Corporation",
            Cik = "0001045810",
        };
        _dbContext.Add(_company);
        _dbContext.SaveChanges();
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task Backfill_PendingNvidiaFiling_ReplacesContentAndRechunksAtCurrentVersion()
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
        await _documentProcessor
            .Received(1)
            .ProcessDocuments(
                Arg.Is<List<Document>>(documents =>
                    documents.Count == 1 && documents[0].Id == document.Id
                ),
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
    public async Task Backfill_WhenNormalizedBytesAreUnchanged_RechunksWithoutReplacingFile()
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
        await _documentProcessor
            .Received(1)
            .ProcessDocuments(
                Arg.Is<List<Document>>(documents => documents.Single().Id == document.Id),
                Arg.Any<CancellationToken>()
            );
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

    [Fact]
    public async Task Backfill_CancellationDuringRechunk_PropagatesAfterProcessorReturns()
    {
        var document = SeedDocument(normalizedContentVersion: 0);
        using var cancellation = new CancellationTokenSource();
        _secEdgarClient
            .GetDocumentContent(
                document.AccessionNumber,
                _company.Cik,
                Arg.Any<CancellationToken>()
            )
            .Returns(NvidiaSubmission);
        _documentProcessor
            .ProcessDocuments(Arg.Any<List<Document>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                cancellation.Cancel();
                return Task.CompletedTask;
            });

        var act = () => BuildSut().Backfill(batchSize: 1, cancellationToken: cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private DocumentNormalizationBackfillService BuildSut() =>
        new(
            new DocumentRepository(_dbContext),
            _secEdgarClient,
            new SecDocumentHtmlNormalizer(),
            new SecDocumentHtmlToMarkdownConverter(),
            _fileManager,
            _persistenceService,
            _documentProcessor,
            Substitute.For<ILogger<DocumentNormalizationBackfillService>>()
        );

    private Document SeedDocument(int normalizedContentVersion)
    {
        var document = new Document
        {
            Id = Guid.NewGuid(),
            CommonStockId = _company.Id,
            DocumentType = DocumentType.TenK,
            ReportingDate = new DateOnly(2026, 2, 25),
            ReportingForDate = new DateOnly(2026, 1, 25),
            AccessionNumber = "0001045810-26-000021",
            SourceUrl = "https://www.sec.gov/Archives/edgar/data/1045810/0001045810-26-000021.txt",
            NormalizedContentVersion = normalizedContentVersion,
            Content = new Equibles.Media.Data.Models.File
            {
                Name = "nvda-20260125",
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
