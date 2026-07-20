using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Integrations.Sec.Contracts;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.Data;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Configuration;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Sec;

// The as-filed HTML backfill's work-set is defined once by
// DocumentRepository.GetPendingAsFiledHtml and shared with the backoffice "pending" metric.
// The backfill used to narrow that set by Worker.MinSyncDate — the live-scraper floor also used
// by the public-site history clamp — so every 8-K reported before the floor was counted as
// pending on the dashboard but never selected by the worker. Those rows sat at zero attempts
// indefinitely and the metric could never drain (420 stranded 8-Ks in production). The same
// defect was fixed once in the sibling XBRL backfill and reintroduced here by copy. Selection
// must not depend on ReportingDate.
public class AsFiledHtmlBackfillServiceReportingDateTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly DocumentRepository _repository;
    private readonly ISecEdgarClient _secEdgarClient;
    private readonly AsFiledHtmlBackfillService _service;
    private readonly CommonStock _company;

    public AsFiledHtmlBackfillServiceReportingDateTests()
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
        _repository = new DocumentRepository(_dbContext);

        _company = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        _dbContext.Add(_company);
        _dbContext.SaveChanges();

        // The fetch is stubbed to fail: the backfill counts an attempt as Processed BEFORE it
        // reaches EDGAR, so a failing fetch still proves the document was SELECTED — which is the
        // only thing this test is about. It also keeps the real stitcher (and its image
        // downloads) out of the test entirely.
        _secEdgarClient = Substitute.For<ISecEdgarClient>();
        _secEdgarClient
            .GetDocumentContent(Arg.Any<string>(), Arg.Any<string>())
            .Returns<Task<string>>(_ => throw new InvalidOperationException("EDGAR unavailable"));

        var captureService = new AsFiledHtmlCaptureService(
            Options.Create(new AsFiledHtmlCaptureOptions()),
            _secEdgarClient,
            Substitute.For<ILogger<AsFiledHtmlCaptureService>>()
        );

        _service = new AsFiledHtmlBackfillService(
            _repository,
            _secEdgarClient,
            captureService,
            Substitute.For<IDocumentPersistenceService>(),
            Substitute.For<ILogger<AsFiledHtmlBackfillService>>()
        );
    }

    public void Dispose() => _dbContext.Dispose();

    private void SeedPendingEightK(DateOnly reportingDate, string accessionNumber)
    {
        _dbContext.Add(
            new Document
            {
                Id = Guid.NewGuid(),
                CommonStockId = _company.Id,
                DocumentType = DocumentType.EightK,
                ReportingDate = reportingDate,
                AccessionNumber = accessionNumber,
                SourceUrl =
                    $"https://www.sec.gov/Archives/edgar/data/320193/{accessionNumber}.txt",
                AsFiledHtmlVersion = 0,
                AsFiledHtmlAttempts = 0,
            }
        );
        _dbContext.SaveChanges();
    }

    [Fact]
    public async Task Backfill_SelectsPendingEightKsFiledBeforeTheLiveScraperSyncFloor()
    {
        // Worker.MinSyncDate is 2020-01-01 in production; both of these must be selected.
        SeedPendingEightK(new DateOnly(2015, 6, 1), "0001193125-15-000001");
        SeedPendingEightK(new DateOnly(2025, 6, 1), "0001193125-25-000001");

        var result = await _service.Backfill(batchSize: 10);

        Assert.Equal(2, result.Processed);
    }

    [Fact]
    public async Task Backfill_SelectsAPendingEightKEvenWhenEveryCandidatePredatesTheSyncFloor()
    {
        // The production shape: the post-floor corpus is fully stitched, so the only rows left
        // are pre-floor ones. Under the old floor this batch came back empty forever.
        SeedPendingEightK(new DateOnly(2000, 7, 14), "0001193125-00-000001");
        SeedPendingEightK(new DateOnly(2019, 12, 3), "0001193125-19-000001");

        var result = await _service.Backfill(batchSize: 10);

        Assert.Equal(2, result.Processed);
    }
}
