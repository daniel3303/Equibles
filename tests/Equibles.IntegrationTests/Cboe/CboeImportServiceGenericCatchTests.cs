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
/// Sibling to <see cref="CboeImportServiceResilienceTests"/>, which pins the
/// <c>catch (HttpRequestException)</c> branch (log-warn, no escalation). This
/// pins the *generic* <c>catch (Exception)</c> branch in
/// <c>ImportAllPutCallRatios</c>: a non-HTTP failure (parse error, provider
/// bug) must be escalated to <see cref="ErrorReporter"/> AND the loop must keep
/// importing the remaining four CSV types. A regression that merged the two
/// catches would either page on every transient blip or silently drop
/// data-corruption signals — opposite failure modes, both bad.
/// </summary>
public class CboeImportServiceGenericCatchTests
{
    [Fact]
    public async Task Import_PutCallDownloadThrowsNonHttp_EscalatesToErrorReporterAndContinues()
    {
        var client = Substitute.For<ICboeClient>();
        // Total throws a NON-HTTP exception -> must hit the generic catch.
        client
            .DownloadPutCallRatios(CboePutCallCsvType.Total)
            .Returns<Task<List<CboePutCallRecord>>>(_ =>
                throw new InvalidOperationException("malformed CBOE CSV header")
            );
        client
            .DownloadPutCallRatios(Arg.Is<CboePutCallCsvType>(t => t != CboePutCallCsvType.Total))
            .Returns(new List<CboePutCallRecord>());
        client.DownloadVixHistory().Returns(new List<CboeVixRecord>());

        // The generic catch awaits _errorReporter.Report, which creates a scope.
        // A distinct factory for the reporter lets us prove Report was invoked
        // (the HttpRequestException branch never calls it). ErrorReporter
        // swallows the unresolved-ErrorManager throw internally, so this is safe.
        var reporterScopeFactory = ServiceScopeSubstitute.Create();
        var errorReporter = new ErrorReporter(
            reporterScopeFactory,
            Substitute.For<ILogger<ErrorReporter>>()
        );
        var sut = new CboeImportService(
            ServiceScopeSubstitute.Create(),
            Substitute.For<ILogger<CboeImportService>>(),
            client,
            errorReporter
        );

        await sut.Import(CancellationToken.None);

        // Loop kept going past the throwing type.
        await client.Received(1).DownloadPutCallRatios(CboePutCallCsvType.Total);
        await client.Received(1).DownloadPutCallRatios(CboePutCallCsvType.Equity);
        await client.Received(1).DownloadPutCallRatios(CboePutCallCsvType.Index);
        await client.Received(1).DownloadPutCallRatios(CboePutCallCsvType.Vix);
        await client.Received(1).DownloadPutCallRatios(CboePutCallCsvType.Etp);
        // Generic catch escalated to ErrorReporter (HTTP branch never would).
        reporterScopeFactory.Received().CreateScope();
    }
}
