using Equibles.Cboe.HostedService.Services;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Cboe.Contracts;
using Equibles.Integrations.Cboe.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Cboe;

public class CboeImportServiceTests
{
    [Fact]
    public async Task Import_AllDownloadsEmpty_DoesNotCreateDbScope()
    {
        // Each ImportPutCallRatio call short-circuits at `if (records.Count == 0) return`
        // before touching _scopeFactory.CreateScope(). Same pattern in ImportVixHistory.
        // The early-return matters because the scraper runs on a schedule against CBOE's
        // public CDN — if the CDN serves an empty CSV (cache miss, weekend, holiday),
        // we want the worker to do nothing rather than open a DB scope just to query
        // the latest date. Pin the contract so a refactor that moves the GetLatestDate
        // query above the count-check can't turn every CDN miss into a DB round-trip.
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var cboeClient = Substitute.For<ICboeClient>();
        cboeClient
            .DownloadPutCallRatios(Arg.Any<CboePutCallCsvType>())
            .Returns(new List<CboePutCallRecord>());
        cboeClient.DownloadVixHistory().Returns(new List<CboeVixRecord>());
        var errorReporter = Substitute.For<ErrorReporter>(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<ErrorReporter>>()
        );

        var sut = new CboeImportService(
            scopeFactory,
            Substitute.For<ILogger<CboeImportService>>(),
            cboeClient,
            errorReporter
        );

        await sut.Import(CancellationToken.None);

        scopeFactory.DidNotReceive().CreateScope();
    }
}
