using Equibles.Cftc.Data;
using Equibles.Cftc.HostedService.Services;
using Equibles.Cftc.Repositories;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Cftc.Contracts;
using Equibles.Integrations.Cftc.Models;
using Equibles.IntegrationTests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Cftc;

/// <summary>
/// The full-pipeline test pins the happy <c>ImportYear</c>. The per-year
/// <c>catch (Exception)</c> branch in <c>Import</c> — a non-HTTP failure must
/// log-error, escalate to <see cref="ErrorReporter"/>, and let the loop carry
/// on to the next year — is uncovered. A regression that merged this with the
/// sibling <c>catch (HttpRequestException)</c> (warn + skip, no escalation)
/// would silently drop data-corruption signals from a bad yearly report.
/// </summary>
public class CftcImportServiceGenericCatchTests
{
    [Fact]
    public async Task Import_ImportYearThrowsNonHttp_EscalatesToErrorReporterAndContinues()
    {
        // Empty DB + MinSyncDate this year ⇒ DetermineStartYear == endYear,
        // so the loop runs exactly one year and that year throws non-HTTP.
        var dbContext = TestDbContextFactory.Create(new CftcModuleConfiguration());
        var minSyncDate = new DateTime(DateTime.UtcNow.Year, 1, 1);

        var cftcClient = Substitute.For<ICftcClient>();
        cftcClient
            .DownloadYearlyReport(Arg.Any<int>())
            .Returns<Task<List<CftcReportRecord>>>(_ =>
                throw new InvalidOperationException("malformed CFTC COT column header")
            );

        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(CftcContractRepository), new CftcContractRepository(dbContext)),
            (typeof(CftcPositionReportRepository), new CftcPositionReportRepository(dbContext))
        );
        // Distinct factory for the reporter proves the generic catch escalated
        // (the HttpRequestException branch never calls Report).
        var reporterScopeFactory = ServiceScopeSubstitute.Create();
        var errorReporter = new ErrorReporter(
            reporterScopeFactory,
            Substitute.For<ILogger<ErrorReporter>>()
        );

        var sut = new CftcImportService(
            scopeFactory,
            Substitute.For<ILogger<CftcImportService>>(),
            cftcClient,
            Options.Create(new WorkerOptions { MinSyncDate = minSyncDate }),
            errorReporter
        );

        await sut.Import(CancellationToken.None);

        await cftcClient.Received().DownloadYearlyReport(Arg.Any<int>());
        reporterScopeFactory.Received().CreateScope();
    }
}
