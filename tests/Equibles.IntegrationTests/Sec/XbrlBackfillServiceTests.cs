using Equibles.CommonStocks.Data.Models;
using Equibles.Integrations.Sec.Contracts;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Media.Repositories;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Configuration;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSubstitute;
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

    private async Task SeedDocument(Guid companyId, string accessionNumber, DateOnly reportingDate)
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
            new FileManager(new FileRepository(DbContext))
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
    public async Task Backfill_SkipsDocumentsWithoutAccession()
    {
        var company = await SeedCompany();
        await SeedDocument(company.Id, accessionNumber: null, new DateOnly(2024, 1, 1));
        DbContext.ChangeTracker.Clear();

        var result = await BuildSut(StubClient(InlineSubmission)).Backfill(batchSize: 10, null);

        result.Processed.Should().Be(0);
    }
}
