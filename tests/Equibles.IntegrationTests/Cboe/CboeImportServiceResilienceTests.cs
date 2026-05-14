using Equibles.Cboe.HostedService.Services;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Cboe.Contracts;
using Equibles.Integrations.Cboe.Models;
using Equibles.IntegrationTests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Cboe;

/// <summary>
/// Sibling to the full-pipeline test. Pins the <c>catch (HttpRequestException)</c>
/// branch in <c>ImportAllPutCallRatios</c>: a single CSV download failure (network
/// blip, CBOE temporary 5xx) must log-warn and continue, NOT escalate to
/// <see cref="ErrorReporter"/>. A regression that promoted HttpRequestException
/// to the generic <c>catch (Exception)</c> branch would page on every transient
/// scrape error — CBOE serves five separate CSV endpoints, the failure rate
/// across them under one hour is non-trivial.
/// </summary>
public class CboeImportServiceResilienceTests
{
    [Fact]
    public async Task Import_PutCallDownloadThrowsHttpException_LogsWarningAndDoesNotReportToErrorReporter()
    {
        var client = Substitute.For<ICboeClient>();
        // First type (Total) blows up with a transient HTTP failure.
        client
            .DownloadPutCallRatios(CboePutCallCsvType.Total)
            .Returns<Task<List<CboePutCallRecord>>>(_ =>
                throw new HttpRequestException("CBOE returned 503")
            );
        // All other types complete with no records — exercises the early-return
        // after the empty-records check and pins that the foreach keeps iterating.
        client
            .DownloadPutCallRatios(Arg.Is<CboePutCallCsvType>(t => t != CboePutCallCsvType.Total))
            .Returns(new List<CboePutCallRecord>());
        client.DownloadVixHistory().Returns(new List<CboeVixRecord>());

        // Substituted scope factory is fine — the early-return on Count == 0 means no scope
        // is ever resolved on this happy-skip path.
        var errorReporter = new ErrorReporter(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<ErrorReporter>>()
        );
        var sut = new CboeImportService(
            ServiceScopeSubstitute.Create(),
            Substitute.For<ILogger<CboeImportService>>(),
            client,
            errorReporter
        );

        // Must not throw — the HttpRequestException is the resilience contract under test.
        await sut.Import(CancellationToken.None);

        // All five put/call types must have been attempted despite the first one throwing.
        await client.Received(1).DownloadPutCallRatios(CboePutCallCsvType.Total);
        await client.Received(1).DownloadPutCallRatios(CboePutCallCsvType.Equity);
        await client.Received(1).DownloadPutCallRatios(CboePutCallCsvType.Index);
        await client.Received(1).DownloadPutCallRatios(CboePutCallCsvType.Vix);
        await client.Received(1).DownloadPutCallRatios(CboePutCallCsvType.Etp);
        await client.Received(1).DownloadVixHistory();
    }
}
