using System.Reflection;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Rollback-deploy pin for the version-stamped ledger: the contract treats a data set as
/// processed when stamped AT OR ABOVE the current parser version, so a row stamped by a newer
/// binary (after rolling back to an older one) must still read as processed — a regression to
/// an equality check would re-import the whole history on every rollback.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HoldingsScraperWorkerIsAlreadyProcessedFutureVersionTests : ParadeDbMcpTestBase
{
    public HoldingsScraperWorkerIsAlreadyProcessedFutureVersionTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task IsAlreadyProcessed_RowStampedAboveCurrentParserVersion_ReturnsTrue()
    {
        DbContext.Add(
            new ProcessedDataSet
            {
                FileName = "2025q1_form13f.zip",
                ParserVersion = ProcessedDataSet.CurrentParserVersion + 1,
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var worker = CreateWorker();
        var method = typeof(HoldingsScraperWorker).GetMethod(
            "IsAlreadyProcessed",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        var futureVersionHit = await (Task<bool>)method.Invoke(worker, ["2025q1_form13f.zip"]);

        futureVersionHit.Should().BeTrue();
    }

    private HoldingsScraperWorker CreateWorker()
    {
        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(ProcessedDataSetRepository), new ProcessedDataSetRepository(DbContext))
        );
        var configuration = Substitute.For<IConfiguration>();
        configuration["Sec:ContactEmail"].Returns("test@example.com");

        return new HoldingsScraperWorker(
            Substitute.For<ILogger<HoldingsScraperWorker>>(),
            scopeFactory,
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new WorkerOptions()),
            configuration,
            new HoldingsRescanSignal()
        );
    }
}
