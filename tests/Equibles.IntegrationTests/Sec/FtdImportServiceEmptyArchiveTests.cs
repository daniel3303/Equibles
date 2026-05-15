using System.IO.Compression;
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
/// Sibling to the recent-404-skip pin. The full-pipeline test always serves a
/// valid zip; the empty-archive branch of <c>DownloadAndParse</c> — SEC
/// returns a 200 zip with zero entries (format change / truncated upload) — is
/// uncovered. That branch must log-error, escalate to <see cref="ErrorReporter"/>,
/// and return [] so the loop skips the file; a regression that dropped the
/// <c>entry == null</c> guard would NRE on <c>entry.Open()</c> and crash the
/// whole FTD scrape the first time SEC ships a malformed archive.
/// </summary>
public class FtdImportServiceEmptyArchiveTests
{
    [Fact]
    public async Task Import_DownloadReturnsEmptyZip_EscalatesAndSkipsWithoutThrowing()
    {
        var dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new SecTestModuleConfiguration()
        );
        var firstOfThisMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);

        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        secEdgarClient
            .DownloadStream(Arg.Any<string>())
            .Returns(_ => Task.FromResult<Stream>(BuildEmptyZip()));

        var tickerMapScopeFactory = ServiceScopeSubstitute.Create(
            (typeof(CommonStockRepository), new CommonStockRepository(dbContext))
        );
        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(FailToDeliverRepository), new FailToDeliverRepository(dbContext)),
            (typeof(TickerMapService), new TickerMapService(tickerMapScopeFactory))
        );
        // Distinct factory for the reporter proves the empty-archive escalation
        // ran (the loop's other skip paths never call Report).
        var reporterScopeFactory = ServiceScopeSubstitute.Create();
        var errorReporter = new ErrorReporter(
            reporterScopeFactory,
            Substitute.For<ILogger<ErrorReporter>>()
        );

        var sut = new FtdImportService(
            scopeFactory,
            secEdgarClient,
            Substitute.For<ILogger<FtdImportService>>(),
            errorReporter,
            Options.Create(new WorkerOptions { MinSyncDate = firstOfThisMonth, TickersToSync = [] })
        );

        await sut.Import(CancellationToken.None);

        reporterScopeFactory.Received().CreateScope();
        dbContext.Set<FailToDeliver>().ToList().Should().BeEmpty();
    }

    private static MemoryStream BuildEmptyZip()
    {
        var stream = new MemoryStream();
        using (new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true)) { }
        stream.Position = 0;
        return stream;
    }
}
