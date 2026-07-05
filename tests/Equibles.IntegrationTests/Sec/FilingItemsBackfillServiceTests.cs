using Equibles.CommonStocks.Data.Models;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Exercises <see cref="FilingItemsBackfillService"/> against real Postgres: pending 8-Ks
/// (null <c>Items</c>) get their item list stamped from the re-fetched submissions feed by
/// accession number, feed misses are marked with the empty-string terminal value so the
/// corpus drains, and a feed failure leaves the company pending for a later retry.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FilingItemsBackfillServiceTests : ParadeDbMcpTestBase
{
    public FilingItemsBackfillServiceTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private async Task<CommonStock> SeedCompany(string ticker, string cik)
    {
        var company = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = $"{ticker} Inc.",
            Cik = cik,
        };
        await using var seed = Fixture.CreateDbContext();
        seed.Set<CommonStock>().Add(company);
        await seed.SaveChangesAsync();
        return company;
    }

    private async Task<Guid> SeedEightK(
        Guid companyId,
        string accessionNumber,
        DateOnly reportingDate,
        string sourceUrl = null,
        string items = null,
        DocumentType documentType = null
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
        var document = new Document
        {
            Id = Guid.NewGuid(),
            CommonStockId = companyId,
            Content = content,
            DocumentType = documentType ?? DocumentType.EightK,
            ReportingDate = reportingDate,
            ReportingForDate = reportingDate,
            AccessionNumber = accessionNumber,
            SourceUrl = sourceUrl,
            Items = items,
        };
        seed.Set<Document>().Add(document);
        await seed.SaveChangesAsync();
        return document.Id;
    }

    private FilingItemsBackfillService BuildSut(ISecEdgarClient secEdgarClient) =>
        new(
            new DocumentRepository(DbContext),
            secEdgarClient,
            NullLogger<FilingItemsBackfillService>()
        );

    private static ISecEdgarClient StubClient(params FilingData[] filings)
    {
        var client = Substitute.For<ISecEdgarClient>();
        client
            .GetCompanyFilings(
                Arg.Any<string>(),
                Arg.Any<DocumentTypeFilter?>(),
                Arg.Any<DateOnly?>(),
                Arg.Any<DateOnly?>()
            )
            .Returns(filings.ToList());
        return client;
    }

    private static FilingData EightKFiling(
        string accessionNumber,
        string items,
        string form = "8-K"
    ) =>
        new()
        {
            Cik = "0000320193",
            AccessionNumber = accessionNumber,
            Form = form,
            Items = items,
            FilingDate = new DateOnly(2024, 2, 1),
            ReportDate = new DateOnly(2024, 2, 1),
        };

    [Fact]
    public async Task Backfill_StampsPendingEightKsByAccessionAndMarksFeedlessOnes()
    {
        var company = await SeedCompany("ITM1", "0000320193");
        await SeedEightK(company.Id, "0000320193-24-000001", new DateOnly(2024, 2, 1));
        await SeedEightK(company.Id, "0000320193-24-000002", new DateOnly(2024, 5, 1));
        DbContext.ChangeTracker.Clear();

        var client = StubClient(
            EightKFiling("0000320193-24-000001", "2.02,9.01"),
            EightKFiling("0000320193-24-000002", null)
        );
        var result = await BuildSut(client).Backfill(companyBatchSize: 10);

        result.Companies.Should().Be(1);
        result.Stamped.Should().Be(1);
        result.NotFound.Should().Be(1);

        await using var verify = Fixture.CreateDbContext();
        var stampedDoc = await verify
            .Set<Document>()
            .SingleAsync(d => d.AccessionNumber == "0000320193-24-000001");
        var feedlessDoc = await verify
            .Set<Document>()
            .SingleAsync(d => d.AccessionNumber == "0000320193-24-000002");
        stampedDoc.Items.Should().Be("2.02,9.01");
        feedlessDoc.Items.Should().Be(string.Empty);
    }

    [Fact]
    public async Task Backfill_StampsPendingEightKAmendmentsFromTheUnfilteredFeed()
    {
        // 8-K/A rows carry form "8-K/A" in the submissions feed, so an exact-form
        // "8-K" filter would drop them and the amendment would be stamped not-found.
        // The sweep must select pending amendments AND walk the feed unfiltered.
        var company = await SeedCompany("ITMA", "0000320193");
        var documentId = await SeedEightK(
            company.Id,
            "0000320193-24-000003",
            new DateOnly(2024, 3, 1),
            documentType: DocumentType.EightKa
        );
        DbContext.ChangeTracker.Clear();

        var client = StubClient(EightKFiling("0000320193-24-000003", "2.02,9.01", form: "8-K/A"));
        var result = await BuildSut(client).Backfill(companyBatchSize: 10);

        result.Companies.Should().Be(1);
        result.Stamped.Should().Be(1);

        await using var verify = Fixture.CreateDbContext();
        var document = await verify.Set<Document>().SingleAsync(d => d.Id == documentId);
        document.Items.Should().Be("2.02,9.01");

        // Pin the unfiltered walk itself — a form-filtered stub would still return the
        // amendment row above, so assert the service passed no document-type filter.
        await client
            .Received(1)
            .GetCompanyFilings(
                Arg.Any<string>(),
                Arg.Is<DocumentTypeFilter?>(f => f == null),
                Arg.Any<DateOnly?>(),
                Arg.Any<DateOnly?>()
            );
    }

    [Fact]
    public async Task Backfill_DerivesAccessionFromLegacySourceUrlAndPersistsIt()
    {
        var company = await SeedCompany("ITM2", "0000320193");
        var documentId = await SeedEightK(
            company.Id,
            accessionNumber: null,
            new DateOnly(2015, 4, 1),
            sourceUrl: "https://www.sec.gov/Archives/edgar/data/320193/0000320193-15-000005.txt"
        );
        DbContext.ChangeTracker.Clear();

        var client = StubClient(EightKFiling("0000320193-15-000005", "2.02"));
        var result = await BuildSut(client).Backfill(companyBatchSize: 10);

        result.Stamped.Should().Be(1);

        await using var verify = Fixture.CreateDbContext();
        var document = await verify.Set<Document>().SingleAsync(d => d.Id == documentId);
        document.Items.Should().Be("2.02");
        document.AccessionNumber.Should().Be("0000320193-15-000005");
    }

    [Fact]
    public async Task Backfill_TerminalMarkerDrainsTheCorpus()
    {
        // A document the feed can never match (no accession, underivable URL) must be marked
        // on the first sweep so the company is not re-fetched forever.
        var company = await SeedCompany("ITM3", "0000320193");
        await SeedEightK(company.Id, accessionNumber: null, new DateOnly(2024, 2, 1));
        DbContext.ChangeTracker.Clear();

        var client = StubClient(EightKFiling("0000320193-24-000009", "2.02"));
        var sut = BuildSut(client);

        var first = await sut.Backfill(companyBatchSize: 10);
        var second = await sut.Backfill(companyBatchSize: 10);

        first.NotFound.Should().Be(1);
        second.Companies.Should().Be(0);
        await client
            .Received(1)
            .GetCompanyFilings(
                Arg.Any<string>(),
                Arg.Any<DocumentTypeFilter?>(),
                Arg.Any<DateOnly?>(),
                Arg.Any<DateOnly?>()
            );
    }

    [Fact]
    public async Task Backfill_FeedFailure_LeavesDocumentsPendingForRetry()
    {
        var company = await SeedCompany("ITM4", "0000320193");
        var documentId = await SeedEightK(
            company.Id,
            "0000320193-24-000010",
            new DateOnly(2024, 2, 1)
        );
        DbContext.ChangeTracker.Clear();

        var failing = Substitute.For<ISecEdgarClient>();
        failing
            .GetCompanyFilings(
                Arg.Any<string>(),
                Arg.Any<DocumentTypeFilter?>(),
                Arg.Any<DateOnly?>(),
                Arg.Any<DateOnly?>()
            )
            .ThrowsAsync(new HttpRequestException("EDGAR unavailable"));
        var result = await BuildSut(failing).Backfill(companyBatchSize: 10);

        result.Failed.Should().Be(1);
        result.Companies.Should().Be(0);

        await using (var verify = Fixture.CreateDbContext())
        {
            var document = await verify.Set<Document>().SingleAsync(d => d.Id == documentId);
            document.Items.Should().BeNull("a failed company must stay pending for retry");
        }

        // A later cycle with a healthy feed picks the company up again and stamps it.
        DbContext.ChangeTracker.Clear();
        var retry = await BuildSut(StubClient(EightKFiling("0000320193-24-000010", "2.02")))
            .Backfill(companyBatchSize: 10);
        retry.Stamped.Should().Be(1);
    }

    [Fact]
    public async Task Backfill_BatchSizeBoundsCompaniesPerCycle_NewestFilingsFirst()
    {
        var older = await SeedCompany("ITM5", "0000100001");
        var newer = await SeedCompany("ITM6", "0000100002");
        await SeedEightK(older.Id, "0000100001-20-000001", new DateOnly(2020, 1, 1));
        await SeedEightK(newer.Id, "0000100002-24-000001", new DateOnly(2024, 1, 1));
        DbContext.ChangeTracker.Clear();

        var client = StubClient(
            EightKFiling("0000100001-20-000001", "2.02"),
            EightKFiling("0000100002-24-000001", "2.02")
        );
        var result = await BuildSut(client).Backfill(companyBatchSize: 1);

        result.Companies.Should().Be(1);

        await using var verify = Fixture.CreateDbContext();
        var newerDoc = await verify
            .Set<Document>()
            .SingleAsync(d => d.AccessionNumber == "0000100002-24-000001");
        var olderDoc = await verify
            .Set<Document>()
            .SingleAsync(d => d.AccessionNumber == "0000100001-20-000001");
        newerDoc.Items.Should().Be("2.02", "the company with the newest pending 8-K goes first");
        olderDoc.Items.Should().BeNull();
    }

    [Fact]
    public async Task Backfill_OneFailingCompany_DoesNotAbortTheRestOfTheBatch()
    {
        // The newest-first ordering puts the failing company first; the healthy one must
        // still be fetched and stamped in the same cycle.
        var failingCompany = await SeedCompany("ITM8", "0000200001");
        var healthyCompany = await SeedCompany("ITM9", "0000200002");
        await SeedEightK(failingCompany.Id, "0000200001-24-000001", new DateOnly(2024, 6, 1));
        await SeedEightK(healthyCompany.Id, "0000200002-24-000001", new DateOnly(2024, 1, 1));
        DbContext.ChangeTracker.Clear();

        var client = Substitute.For<ISecEdgarClient>();
        client
            .GetCompanyFilings(
                "0000200001",
                Arg.Any<DocumentTypeFilter?>(),
                Arg.Any<DateOnly?>(),
                Arg.Any<DateOnly?>()
            )
            .ThrowsAsync(new HttpRequestException("EDGAR unavailable"));
        client
            .GetCompanyFilings(
                "0000200002",
                Arg.Any<DocumentTypeFilter?>(),
                Arg.Any<DateOnly?>(),
                Arg.Any<DateOnly?>()
            )
            .Returns([EightKFiling("0000200002-24-000001", "2.02")]);
        var result = await BuildSut(client).Backfill(companyBatchSize: 10);

        result.Failed.Should().Be(1);
        result.Companies.Should().Be(1);
        result.Stamped.Should().Be(1);

        await using var verify = Fixture.CreateDbContext();
        var failed = await verify
            .Set<Document>()
            .SingleAsync(d => d.AccessionNumber == "0000200001-24-000001");
        var stamped = await verify
            .Set<Document>()
            .SingleAsync(d => d.AccessionNumber == "0000200002-24-000001");
        failed.Items.Should().BeNull();
        stamped.Items.Should().Be("2.02");
    }

    [Fact]
    public async Task Backfill_CompanyWithoutCik_IsNeverSelected()
    {
        var company = await SeedCompany("ITM7", cik: null);
        await SeedEightK(company.Id, "0000100003-24-000001", new DateOnly(2024, 1, 1));
        DbContext.ChangeTracker.Clear();

        var client = StubClient();
        var result = await BuildSut(client).Backfill(companyBatchSize: 10);

        result.Companies.Should().Be(0);
        await client
            .DidNotReceive()
            .GetCompanyFilings(
                Arg.Any<string>(),
                Arg.Any<DocumentTypeFilter?>(),
                Arg.Any<DateOnly?>(),
                Arg.Any<DateOnly?>()
            );
    }
}
