using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.Integrations.Sec.Contracts;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Media.Repositories;
using Equibles.Sec.BusinessLogic;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Sec;

[Collection(ParadeDbCollection.Name)]
public class DocumentNormalizationBackfillPostgresTests : ParadeDbMcpTestBase
{
    private const string Accession = "0001045810-26-000021";
    private const string SourceUrl =
        "https://www.sec.gov/Archives/edgar/data/1045810/0001045810-26-000021.txt";

    public DocumentNormalizationBackfillPostgresTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Backfill_ReplacesFileDeletesStaleChunksAndQueuesLockedRechunking()
    {
        var document = await SeedLegacyDocument("NVDA");
        var oldContentId = document.ContentId;
        DbContext
            .Set<Chunk>()
            .Add(
                new Chunk
                {
                    DocumentId = document.Id,
                    Index = 0,
                    StartPosition = 0,
                    EndPosition = 3,
                    StartLineNumber = 1,
                    Content = "old",
                    DocumentType = DocumentType.TenK,
                    Ticker = "NVDA",
                    ReportingDate = new DateTime(2026, 2, 25, 0, 0, 0, DateTimeKind.Utc),
                }
            );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var secClient = Substitute.For<ISecEdgarClient>();
        secClient
            .GetDocumentContent(Accession, "0001045810", Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await using var concurrent = Fixture.CreateDbContext();
                await concurrent
                    .Set<Document>()
                    .Where(d => d.Id == document.Id)
                    .ExecuteUpdateAsync(setters =>
                        setters.SetProperty(d => d.Items, "concurrent-enrichment")
                    );
                return NvidiaSubmission;
            });

        var result = await BuildSut(secClient).Backfill(batchSize: 1);

        result.Replaced.Should().Be(1);
        await using var verify = Fixture.CreateDbContext();
        var saved = await verify
            .Set<Document>()
            .Include(d => d.Content)
                .ThenInclude(f => f.FileContent)
            .SingleAsync(d => d.Id == document.Id);
        saved.AccessionNumber.Should().Be(Accession);
        saved.NormalizedContentVersion.Should().Be(Document.NormalizedContentBuilderVersion);
        saved.ContentId.Should().NotBe(oldContentId);
        saved.Items.Should().Be("concurrent-enrichment");
        Encoding
            .UTF8.GetString(saved.Content.FileContent.Bytes)
            .Should()
            .Contain("| Compute & Networking | 193,479 | 116,193 | 77,286 | 67 | % |");
        (await verify.Set<Equibles.Media.Data.Models.File>().AnyAsync(f => f.Id == oldContentId))
            .Should()
            .BeFalse();
        var chunks = await verify
            .Set<Chunk>()
            .Where(c => c.DocumentId == document.Id)
            .OrderBy(c => c.Index)
            .ToListAsync();
        chunks.Should().BeEmpty();
        saved.ChunkedAt.Should().BeNull();
    }

    [Fact]
    public async Task Backfill_FailurePersistsDerivedAccessionAndStopsAtRetryCeiling()
    {
        var document = await SeedLegacyDocument("NVDAF");
        var secClient = Substitute.For<ISecEdgarClient>();
        secClient
            .GetDocumentContent(Accession, "0001045810", Arg.Any<CancellationToken>())
            .Returns<Task<string>>(_ => throw new HttpRequestException("SEC unavailable"));
        var sut = BuildSut(secClient);

        for (var attempt = 0; attempt < Document.MaxNormalizedContentAttempts; attempt++)
        {
            var result = await sut.Backfill(batchSize: 1);
            result.Failed.Should().Be(1);
        }

        var terminal = await sut.Backfill(batchSize: 1);

        terminal.Processed.Should().Be(0);
        await using var verify = Fixture.CreateDbContext();
        var saved = await verify.Set<Document>().SingleAsync(d => d.Id == document.Id);
        saved.AccessionNumber.Should().Be(Accession);
        saved.NormalizedContentAttempts.Should().Be(Document.MaxNormalizedContentAttempts);
        saved.NormalizedContentVersion.Should().Be(0);
    }

    [Fact]
    public async Task Backfill_PersistenceFailurePersistsAttemptsAndStopsAtRetryCeiling()
    {
        var document = await SeedLegacyDocument("NVDAP");
        var secClient = Substitute.For<ISecEdgarClient>();
        secClient
            .GetDocumentContent(Accession, "0001045810", Arg.Any<CancellationToken>())
            .Returns(NvidiaSubmission);
        var persistence = Substitute.For<IDocumentPersistenceService>();
        persistence
            .ReplaceContent(Arg.Any<Document>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new IOException("content store unavailable"));
        var sut = BuildSut(secClient, persistence);

        for (var attempt = 0; attempt < Document.MaxNormalizedContentAttempts; attempt++)
        {
            var result = await sut.Backfill(batchSize: 1);
            result.Failed.Should().Be(1);
        }

        var terminal = await sut.Backfill(batchSize: 1);

        terminal.Processed.Should().Be(0);
        await using var verify = Fixture.CreateDbContext();
        var saved = await verify.Set<Document>().SingleAsync(d => d.Id == document.Id);
        saved.NormalizedContentAttempts.Should().Be(Document.MaxNormalizedContentAttempts);
        saved.NormalizedContentVersion.Should().Be(0);
    }

    [Fact]
    public async Task Backfill_UnchangedFileKeepsBlobAndQueuesLockedRechunking()
    {
        var normalized = new SecDocumentHtmlToMarkdownConverter().Convert(
            new SecDocumentHtmlNormalizer().Normalize(NvidiaSubmission)
        );
        var document = await SeedLegacyDocument("NVDAU", Encoding.UTF8.GetBytes(normalized));
        var originalContentId = document.ContentId;
        DbContext
            .Set<Chunk>()
            .Add(
                new Chunk
                {
                    DocumentId = document.Id,
                    Index = 0,
                    StartPosition = 0,
                    EndPosition = normalized.Length,
                    StartLineNumber = 1,
                    Content = normalized.Replace('\n', ' '),
                    DocumentType = DocumentType.TenK,
                    Ticker = "NVDAU",
                    ReportingDate = new DateTime(2026, 2, 25, 0, 0, 0, DateTimeKind.Utc),
                }
            );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        var secClient = Substitute.For<ISecEdgarClient>();
        secClient
            .GetDocumentContent(Accession, "0001045810", Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await using var concurrent = Fixture.CreateDbContext();
                await concurrent
                    .Set<Document>()
                    .Where(d => d.Id == document.Id)
                    .ExecuteUpdateAsync(setters =>
                        setters.SetProperty(d => d.Items, "concurrent-enrichment")
                    );
                return NvidiaSubmission;
            });

        var result = await BuildSut(secClient).Backfill(batchSize: 1);

        result.Unchanged.Should().Be(1);
        result.Replaced.Should().Be(0);
        await using var verify = Fixture.CreateDbContext();
        var saved = await verify.Set<Document>().SingleAsync(d => d.Id == document.Id);
        saved.ContentId.Should().Be(originalContentId);
        saved.Items.Should().Be("concurrent-enrichment");
        var chunks = await verify
            .Set<Chunk>()
            .Where(c => c.DocumentId == document.Id)
            .OrderBy(c => c.Index)
            .ToListAsync();
        chunks.Should().BeEmpty();
        saved.ChunkedAt.Should().BeNull();
    }

    private async Task<Document> SeedLegacyDocument(string ticker, byte[] content = null)
    {
        var company = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = "NVIDIA Corporation",
            Cik = "0001045810",
        };
        DbContext.Set<CommonStock>().Add(company);
        await DbContext.SaveChangesAsync();

        var fileManager = CreateFileManager();
        await CreatePersistence(fileManager)
            .Save(
                company,
                content ?? "legacy table body"u8.ToArray(),
                $"{ticker}-2026-10k.txt",
                DocumentType.TenK,
                new DateOnly(2026, 2, 25),
                new DateOnly(2026, 1, 25),
                SourceUrl,
                accessionNumber: ""
            );

        var document = await DbContext
            .Set<Document>()
            .SingleAsync(d => d.CommonStockId == company.Id);
        document.NormalizedContentVersion = 0;
        await DbContext.SaveChangesAsync();
        return document;
    }

    private DocumentNormalizationBackfillService BuildSut(
        ISecEdgarClient secClient,
        IDocumentPersistenceService persistenceService = null
    )
    {
        var fileManager = CreateFileManager();
        return new DocumentNormalizationBackfillService(
            new DocumentRepository(DbContext),
            secClient,
            new SecDocumentHtmlNormalizer(),
            new SecDocumentHtmlToMarkdownConverter(),
            fileManager,
            persistenceService ?? CreatePersistence(fileManager),
            Substitute.For<ILogger<DocumentNormalizationBackfillService>>()
        );
    }

    private FileManager CreateFileManager() =>
        FileManagerTestFactory.Create(
            new FileRepository(DbContext),
            pendingBlobDeletionRepository: new PendingBlobDeletionRepository(DbContext)
        );

    private DocumentPersistenceService CreatePersistence(FileManager fileManager) =>
        new(
            new DocumentRepository(DbContext),
            new ChunkRepository(DbContext),
            fileManager,
            new DocumentImageService(
                new DocumentImageRepository(DbContext),
                new FileRepository(DbContext),
                fileManager
            ),
            Substitute.For<IBus>()
        );

    [Fact]
    public void OrderedWorkSet_MatchesTheBackfillIndex()
    {
        var sql = new DocumentRepository(DbContext)
            .GetOrderedPendingNormalizedContent(includeAllDocumentTypes: false)
            .Take(16)
            .ToQueryString();

        sql.Should()
            .Contain(
                "ORDER BY d.\"DocumentType\", d.\"NormalizedContentVersion\", d.\"NormalizedContentAttempts\", d.\"ReportingDate\" DESC, d.\"Id\""
            );

        var entity = DbContext
            .GetService<IDesignTimeModel>()
            .Model.FindEntityType(typeof(Document));
        var index = entity.GetIndexes().Single(i => i.Name == "IX_Document_NormalizationBackfill");
        index
            .Properties.Select(property => property.Name)
            .Should()
            .Equal(
                nameof(Document.DocumentType),
                nameof(Document.NormalizedContentVersion),
                nameof(Document.NormalizedContentAttempts),
                nameof(Document.ReportingDate),
                nameof(Document.Id)
            );
        index.IsDescending.Should().Equal(false, false, false, true, false);
    }

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
