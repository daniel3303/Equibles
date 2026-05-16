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
using Xunit;

namespace Equibles.IntegrationTests.Cftc;

/// <summary>
/// Sibling to <see cref="CftcImportServiceGenericCatchTests"/> (which pins the
/// non-HTTP escalation arm). This pins the <c>catch (HttpRequestException)</c>
/// arm of <c>Import</c>: a download failure for one year must warn and skip
/// WITHOUT escalating to the error reporter, and the loop must carry on.
/// </summary>
public class CftcImportServiceHttpCatchTests
{
    [Fact]
    public async Task Import_ImportYearThrowsHttp_WarnsAndSkipsWithoutEscalating()
    {
        // Empty DB + MinSyncDate this year ⇒ exactly one year processed.
        var dbContext = TestDbContextFactory.Create(new CftcModuleConfiguration());
        var minSyncDate = new DateTime(DateTime.UtcNow.Year, 1, 1);

        var cftcClient = Substitute.For<ICftcClient>();
        cftcClient
            .DownloadYearlyReport(Arg.Any<int>())
            .Returns<Task<List<CftcReportRecord>>>(_ =>
                throw new HttpRequestException("CFTC COT endpoint unavailable")
            );

        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(CftcContractRepository), new CftcContractRepository(dbContext)),
            (typeof(CftcPositionReportRepository), new CftcPositionReportRepository(dbContext))
        );
        // Distinct factory for the reporter: the HttpRequestException arm must
        // NOT escalate, so this scope must never be created.
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
        reporterScopeFactory.DidNotReceive().CreateScope();
    }
}
