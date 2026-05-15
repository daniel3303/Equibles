using System.Net;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
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
/// Sibling to <see cref="FtdImportServiceRecent404SkipTests"/> (recent-file 404).
/// Pins the other three zero-hit per-file catch arms of <c>Import</c>: an OLD
/// file 404 (possible URL change → warn), a non-404 HttpRequestException (skip),
/// and a non-HTTP exception (report via ErrorReporter). All must keep the loop
/// alive — one bad file never aborts the FTD scrape.
/// </summary>
public class FtdImportServiceImportCatchTests
{
    private static (IServiceScopeFactory scopeFactory, EquiblesDbContext dbContext) BuildScope()
    {
        var dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new SecTestModuleConfiguration()
        );
        var tickerMapScopeFactory = ServiceScopeSubstitute.Create(
            (typeof(CommonStockRepository), new CommonStockRepository(dbContext))
        );
        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(FailToDeliverRepository), new FailToDeliverRepository(dbContext)),
            (typeof(TickerMapService), new TickerMapService(tickerMapScopeFactory))
        );
        return (scopeFactory, dbContext);
    }

    [Fact]
    public async Task Import_OldFileReturns404_LogsPossibleUrlChangeAndContinues()
    {
        var (scopeFactory, dbContext) = BuildScope();
        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        secEdgarClient
            .DownloadStream(Arg.Any<string>())
            .Returns<Task<Stream>>(_ =>
                throw new HttpRequestException("Not Found", null, HttpStatusCode.NotFound)
            );

        var sut = new FtdImportService(
            scopeFactory,
            secEdgarClient,
            Substitute.For<ILogger<FtdImportService>>(),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            // ~6 months back → GetFileNames yields old files, none "recent".
            Options.Create(
                new WorkerOptions
                {
                    MinSyncDate = new DateTime(
                        DateTime.UtcNow.AddMonths(-6).Year,
                        DateTime.UtcNow.AddMonths(-6).Month,
                        1
                    ),
                    TickersToSync = [],
                }
            )
        );

        await sut.Import(CancellationToken.None);

        await secEdgarClient.Received().DownloadStream(Arg.Any<string>());
        dbContext.Set<FailToDeliver>().ToList().Should().BeEmpty();
    }

    [Fact]
    public async Task Import_DownloadReturnsNon404Http_SkipsFileWithoutThrowing()
    {
        var (scopeFactory, dbContext) = BuildScope();
        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        secEdgarClient
            .DownloadStream(Arg.Any<string>())
            .Returns<Task<Stream>>(_ =>
                throw new HttpRequestException(
                    "Server Error",
                    null,
                    HttpStatusCode.InternalServerError
                )
            );

        var sut = new FtdImportService(
            scopeFactory,
            secEdgarClient,
            Substitute.For<ILogger<FtdImportService>>(),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(
                new WorkerOptions
                {
                    MinSyncDate = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1),
                    TickersToSync = [],
                }
            )
        );

        await sut.Import(CancellationToken.None);

        await secEdgarClient.Received().DownloadStream(Arg.Any<string>());
        dbContext.Set<FailToDeliver>().ToList().Should().BeEmpty();
    }

    [Fact]
    public async Task Import_DownloadThrowsNonHttpException_ReportsToErrorReporter()
    {
        var (scopeFactory, dbContext) = BuildScope();
        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        secEdgarClient
            .DownloadStream(Arg.Any<string>())
            .Returns<Task<Stream>>(_ => throw new InvalidOperationException("boom"));

        var errorReporter = Substitute.For<ErrorReporter>(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<ErrorReporter>>()
        );

        var sut = new FtdImportService(
            scopeFactory,
            secEdgarClient,
            Substitute.For<ILogger<FtdImportService>>(),
            errorReporter,
            Options.Create(
                new WorkerOptions
                {
                    MinSyncDate = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1),
                    TickersToSync = [],
                }
            )
        );

        await sut.Import(CancellationToken.None);

        await errorReporter
            .Received()
            .Report(
                ErrorSource.FtdScraper,
                "FtdImport.ProcessFile",
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>()
            );
        dbContext.Set<FailToDeliver>().ToList().Should().BeEmpty();
    }
}
