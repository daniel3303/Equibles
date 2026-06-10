using Equibles.CommonStocks.Data.Models;
using Equibles.Integrations.Sec.Contracts;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Media.Repositories;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Configuration;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Exercises <see cref="XbrlBackfillService"/> against real Postgres: pending
/// (<see cref="XbrlCaptureStatus.NotChecked"/>) documents get their XBRL envelope filled in
/// from the re-fetched submission, while the minimum-sync-date floor, the batch cap, and the
/// "must have an accession" guard bound what is touched.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class XbrlBackfillServiceTests : ParadeDbMcpTestBase
{
    public XbrlBackfillServiceTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private const string InlineSubmission =
        "<DOCUMENT>\n<TYPE>10-K\n<FILENAME>doc.htm\n<TEXT>\n"
        + "<html xmlns:ix=\"http://www.xbrl.org/2013/inlineXBRL\"><body><ix:nonFraction>1</ix:nonFraction></body></html>\n"
        + "</TEXT>\n</DOCUMENT>";

    private async Task<CommonStock> SeedCompany()
    {
        var apple = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        await using var seed = Fixture.CreateDbContext();
        seed.Set<CommonStock>().Add(apple);
        await seed.SaveChangesAsync();
        return apple;
    }

    private async Task SeedDocument(
        Guid companyId,
        string accessionNumber,
        DateOnly reportingDate,
        string sourceUrl = null
    )
    {
        await using var seed = Fixture.CreateDbContext();
        var content = new File
        {
            Id = Guid.NewGuid(),
            Name = "content",
            Extension = "txt",
            ContentType = "text/plain",
            Size = 4,
            FileContent = new() { Bytes = "body"u8.ToArray() },
        };
        seed.Set<File>().Add(content);
        seed.Set<Document>()
            .Add(
                new Document
                {
                    Id = Guid.NewGuid(),
                    CommonStockId = companyId,
                    Content = content,
                    DocumentType = DocumentType.TenK,
                    ReportingDate = reportingDate,
                    ReportingForDate = reportingDate,
                    AccessionNumber = accessionNumber,
                    SourceUrl = sourceUrl,
                    XbrlStatus = XbrlCaptureStatus.NotChecked,
                }
            );
        await seed.SaveChangesAsync();
    }

    private XbrlBackfillService BuildSut(ISecEdgarClient secEdgarClient)
    {
        var repo = new DocumentRepository(DbContext);
        var persistence = new DocumentPersistenceService(
            repo,
            new FileManager(new FileRepository(DbContext)),
            Substitute.For<IBus>()
        );
        var capture = new XbrlEnvelopeCaptureService(
            Options.Create(new XbrlCaptureOptions { Enabled = true }),
            NullLogger<XbrlEnvelopeCaptureService>()
        );
        return new XbrlBackfillService(
            repo,
            secEdgarClient,
            capture,
            persistence,
            NullLogger<XbrlBackfillService>()
        );
    }

    private static ISecEdgarClient StubClient(string content)
    {
        var client = Substitute.For<ISecEdgarClient>();
        client.GetDocumentContent(Arg.Any<string>(), Arg.Any<string>()).Returns(content);
        return client;
    }

    [Fact]
    public async Task Backfill_CapturesPendingDocumentsWithinBatch()
    {
        var company = await SeedCompany();
        await SeedDocument(company.Id, "0000320193-24-000001", new DateOnly(2024, 2, 1));
        await SeedDocument(company.Id, "0000320193-24-000002", new DateOnly(2024, 3, 1));
        DbContext.ChangeTracker.Clear();

        var result = await BuildSut(StubClient(InlineSubmission)).Backfill(batchSize: 10, null);

        result.Captured.Should().Be(2);

        await using var verify = Fixture.CreateDbContext();
        var docs = await verify
            .Set<Document>()
            .Where(d => d.CommonStockId == company.Id)
            .ToListAsync();
        docs.Should().OnlyContain(d => d.XbrlStatus == XbrlCaptureStatus.Captured);
        docs.Should().OnlyContain(d => d.XbrlContentId != null);
        docs.Should().OnlyContain(d => d.XbrlType == XbrlType.InlineIxbrl);
    }

    [Fact]
    public async Task Backfill_RespectsMinimumReportingDate()
    {
        var company = await SeedCompany();
        await SeedDocument(company.Id, "0000320193-20-000001", new DateOnly(2020, 1, 1));
        await SeedDocument(company.Id, "0000320193-24-000003", new DateOnly(2024, 1, 1));
        DbContext.ChangeTracker.Clear();

        var result = await BuildSut(StubClient(InlineSubmission))
            .Backfill(batchSize: 10, minReportingDate: new DateOnly(2023, 1, 1));

        result.Processed.Should().Be(1);

        await using var verify = Fixture.CreateDbContext();
        var stale = await verify
            .Set<Document>()
            .SingleAsync(d => d.AccessionNumber == "0000320193-20-000001");
        stale.XbrlStatus.Should().Be(XbrlCaptureStatus.NotChecked);
    }

    [Fact]
    public async Task Backfill_PermanentlyFailingNewestDocument_DoesNotStarveOlderHealthyOne()
    {
        // A newer document that always fails to fetch must not block the older healthy one
        // forever: after its retry ceiling it leaves the working set and the older one captures.
        var company = await SeedCompany();
        await SeedDocument(company.Id, "FAIL-NEWEST", new DateOnly(2024, 3, 1));
        await SeedDocument(company.Id, "OK-OLDER", new DateOnly(2024, 1, 1));
        DbContext.ChangeTracker.Clear();

        var client = Substitute.For<ISecEdgarClient>();
        client
            .GetDocumentContent("FAIL-NEWEST", Arg.Any<string>())
            .ThrowsAsync(new Exception("gone"));
        client.GetDocumentContent("OK-OLDER", Arg.Any<string>()).Returns(InlineSubmission);
        var sut = BuildSut(client);

        // batchSize 1 means each cycle only the single newest pending doc is taken — the
        // failing one, until it exhausts its attempts.
        for (var cycle = 0; cycle < 6; cycle++)
        {
            await sut.Backfill(batchSize: 1, null);
        }

        await using var verify = Fixture.CreateDbContext();
        var failing = await verify
            .Set<Document>()
            .SingleAsync(d => d.AccessionNumber == "FAIL-NEWEST");
        var healthy = await verify
            .Set<Document>()
            .SingleAsync(d => d.AccessionNumber == "OK-OLDER");
        failing.XbrlStatus.Should().Be(XbrlCaptureStatus.NotChecked);
        healthy.XbrlStatus.Should().Be(XbrlCaptureStatus.Captured);
    }

    [Fact]
    public async Task Backfill_PerpetuallySkippedDocument_DropsOutAfterRetryCeiling()
    {
        // Contract: a document fetched successfully but whose extraction leaves it NotChecked
        // (the "Skipped" outcome) must be bounded by the same retry ceiling (5) as a failing
        // fetch — its bumped attempt count is persisted via UpdateXbrl each cycle, so after the
        // ceiling it leaves the working set and can't starve older documents. Guards against the
        // attempt counter being advanced only on the exception path.
        var company = await SeedCompany();
        await SeedDocument(company.Id, "SKIP-ME", new DateOnly(2024, 1, 1));
        DbContext.ChangeTracker.Clear();

        var repo = new DocumentRepository(DbContext);
        var persistence = new DocumentPersistenceService(
            repo,
            new FileManager(new FileRepository(DbContext)),
            Substitute.For<IBus>()
        );
        // Capture disabled => every fetched submission resolves to NotChecked (the Skipped branch).
        var capture = new XbrlEnvelopeCaptureService(
            Options.Create(new XbrlCaptureOptions { Enabled = false }),
            NullLogger<XbrlEnvelopeCaptureService>()
        );
        var sut = new XbrlBackfillService(
            repo,
            StubClient(InlineSubmission),
            capture,
            persistence,
            NullLogger<XbrlBackfillService>()
        );

        // Five cycles consume the five allowed attempts, each leaving the document Skipped.
        for (var cycle = 0; cycle < 5; cycle++)
        {
            var cycleResult = await sut.Backfill(batchSize: 1, null);
            cycleResult.Skipped.Should().Be(1);
        }

        // Sixth cycle: the document has hit the ceiling and is no longer selected.
        var afterCeiling = await sut.Backfill(batchSize: 1, null);
        afterCeiling.Processed.Should().Be(0);

        await using var verify = Fixture.CreateDbContext();
        var doc = await verify.Set<Document>().SingleAsync(d => d.AccessionNumber == "SKIP-ME");
        doc.XbrlStatus.Should().Be(XbrlCaptureStatus.NotChecked);
        doc.XbrlCaptureAttempts.Should().Be(5);
    }

    [Fact]
    public async Task Backfill_SkipsDocumentsWithoutAccession()
    {
        var company = await SeedCompany();
        await SeedDocument(company.Id, accessionNumber: null, new DateOnly(2024, 1, 1));
        DbContext.ChangeTracker.Clear();

        var result = await BuildSut(StubClient(InlineSubmission)).Backfill(batchSize: 10, null);

        result.Processed.Should().Be(0);
    }

    [Fact]
    public async Task Backfill_DerivesAccessionFromEdgarSourceUrl()
    {
        // Rows ingested before AccessionNumber existed carry it only inside the stored EDGAR
        // submission URL — the backfill must recover it, fetch the filing, and persist the
        // derived accession alongside the capture outcome.
        var company = await SeedCompany();
        await SeedDocument(
            company.Id,
            accessionNumber: null,
            new DateOnly(2024, 2, 1),
            sourceUrl: "https://www.sec.gov/Archives/edgar/data/0000320193/0000320193-24-000123.txt"
        );
        DbContext.ChangeTracker.Clear();

        var client = StubClient(InlineSubmission);
        var result = await BuildSut(client).Backfill(batchSize: 10, null);

        result.Captured.Should().Be(1);
        await client.Received(1).GetDocumentContent("0000320193-24-000123", "0000320193");

        await using var verify = Fixture.CreateDbContext();
        var doc = await verify.Set<Document>().SingleAsync(d => d.CommonStockId == company.Id);
        doc.AccessionNumber.Should().Be("0000320193-24-000123");
        doc.XbrlStatus.Should().Be(XbrlCaptureStatus.Captured);
    }

    [Fact]
    public async Task Backfill_NeverSelectsNonEdgarSourceUrl()
    {
        // Documents from other providers (e.g. earnings-call transcripts) have no filing to
        // re-fetch — they must not be selected, so they can't burn cycles or EDGAR budget.
        var company = await SeedCompany();
        await SeedDocument(
            company.Id,
            accessionNumber: null,
            new DateOnly(2024, 2, 1),
            sourceUrl: "https://www.alphavantage.co/query?function=EARNINGS_CALL_TRANSCRIPT&symbol=AAPL&quarter=2024Q1"
        );
        DbContext.ChangeTracker.Clear();

        var result = await BuildSut(StubClient(InlineSubmission)).Backfill(batchSize: 10, null);

        result.Processed.Should().Be(0);
    }

    [Fact]
    public async Task Backfill_UnparseableEdgarSourceUrl_DropsOutAfterRetryCeiling()
    {
        // An EDGAR-looking URL that doesn't carry a well-formed accession can never be
        // fetched; each cycle records a failure and bumps the attempt count so the document
        // leaves the working set at the ceiling instead of being reselected forever.
        var company = await SeedCompany();
        await SeedDocument(
            company.Id,
            accessionNumber: null,
            new DateOnly(2024, 2, 1),
            sourceUrl: "https://www.sec.gov/Archives/edgar/data/0000320193/index.json"
        );
        DbContext.ChangeTracker.Clear();

        var sut = BuildSut(StubClient(InlineSubmission));
        for (var cycle = 0; cycle < 5; cycle++)
        {
            var cycleResult = await sut.Backfill(batchSize: 1, null);
            cycleResult.Failed.Should().Be(1);
        }

        var afterCeiling = await sut.Backfill(batchSize: 1, null);
        afterCeiling.Processed.Should().Be(0);

        await using var verify = Fixture.CreateDbContext();
        var doc = await verify.Set<Document>().SingleAsync(d => d.CommonStockId == company.Id);
        doc.AccessionNumber.Should().BeNull();
        doc.XbrlCaptureAttempts.Should().Be(5);
    }
}
