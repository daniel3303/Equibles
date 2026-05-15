using System.Net;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Sec.Contracts;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using Equibles.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// The full-pipeline test pins the happy download; the "up to date" test pins
/// the empty-fileNames early exit. The 404-on-a-recent-file resilience branch —
/// <c>catch (HttpRequestException) when StatusCode == NotFound</c> +
/// <c>IsRecentFtdFile</c> true — was uncovered. The SEC publishes each period's
/// FTD zip a few weeks late, so the current month 404s by design every tick
/// until release. That 404 must be swallowed and the loop must continue; a
/// regression that narrowed the <c>when</c> filter or dropped the recency check
/// would crash the FTD scraper daily for weeks each month.
/// </summary>
public class FtdImportServiceRecent404SkipTests
{
    [Fact]
    public async Task Import_RecentFileReturns404_SkipsWithoutThrowingOrPersisting()
    {
        var dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new SecTestModuleConfiguration()
        );

        // No FailToDeliver rows -> SyncDateResolver falls back to MinSyncDate.
        // First-of-this-month makes GetFileNames yield the current month's files,
        // every one of which IsRecentFtdFile classifies as recent.
        var firstOfThisMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);

        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        secEdgarClient
            .DownloadStream(Arg.Any<string>())
            .Returns<Task<Stream>>(_ =>
                throw new HttpRequestException("Not Found", null, HttpStatusCode.NotFound)
            );

        var tickerMapScopeFactory = ServiceScopeSubstitute.Create(
            (typeof(CommonStockRepository), new CommonStockRepository(dbContext))
        );
        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(FailToDeliverRepository), new FailToDeliverRepository(dbContext)),
            (typeof(TickerMapService), new TickerMapService(tickerMapScopeFactory))
        );
        var errorReporter = new ErrorReporter(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<ErrorReporter>>()
        );

        var sut = new FtdImportService(
            scopeFactory,
            secEdgarClient,
            Substitute.For<ILogger<FtdImportService>>(),
            errorReporter,
            Options.Create(new WorkerOptions { MinSyncDate = firstOfThisMonth, TickersToSync = [] })
        );

        // The resilience contract: a recent-file 404 must not propagate.
        await sut.Import(CancellationToken.None);

        // Loop entered (download attempted) yet nothing persisted — the 404 path
        // skipped before ImportRecords.
        await secEdgarClient.Received().DownloadStream(Arg.Any<string>());
        dbContext.Set<FailToDeliver>().ToList().Should().BeEmpty();
    }
}
