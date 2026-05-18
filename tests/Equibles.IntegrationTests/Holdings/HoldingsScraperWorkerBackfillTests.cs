using System.Reflection;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Unit-tier <c>HoldingsScraperWorkerTests</c> covers only constants and
/// <c>ValidateConfiguration</c>. The DB-touching private methods
/// (<c>BackfillProcessedDataSets</c>, <c>IsAlreadyProcessed</c>, <c>MarkAsProcessed</c>,
/// <c>RecalculatePendingValues</c>) are completely uncovered. This integration test
/// pins the seeding contract end-to-end against real Postgres: on first run (empty
/// table), BackfillProcessedDataSets must persist every file name except the last,
/// so the cycle only downloads the most recent period. A regression that drops the
/// <c>.Take(fileNames.Count - 1)</c> (seeding nothing, re-downloading history) or
/// flips it to <c>.Take(fileNames.Count)</c> (seeding everything, never refreshing)
/// would fail here.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HoldingsScraperWorkerBackfillTests : ParadeDbMcpTestBase
{
    public HoldingsScraperWorkerBackfillTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task BackfillProcessedDataSets_EmptyTableMultipleFiles_SeedsAllExceptLast()
    {
        var fileNames = new List<string>
        {
            "2024q1_form13f.zip",
            "2024q2_form13f.zip",
            "2024q3_form13f.zip",
            "2024q4_form13f.zip",
        };

        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(ProcessedDataSetRepository), new ProcessedDataSetRepository(DbContext))
        );

        var configuration = Substitute.For<IConfiguration>();
        configuration["Sec:ContactEmail"].Returns("test@example.com");

        var worker = new HoldingsScraperWorker(
            Substitute.For<ILogger<HoldingsScraperWorker>>(),
            scopeFactory,
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new WorkerOptions()),
            configuration, new HoldingsRescanSignal()
        );

        var method = typeof(HoldingsScraperWorker).GetMethod(
            "BackfillProcessedDataSets",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        await (Task)method.Invoke(worker, [fileNames, CancellationToken.None]);

        // Re-read from a fresh context so the assertion exercises persisted rows and
        // catches any tracker-only state that would NOT survive a process restart.
        await using var verify = Fixture.CreateDbContext();
        var rows = await verify.Set<ProcessedDataSet>().AsNoTracking().ToListAsync();

        rows.Select(r => r.FileName)
            .Should()
            .BeEquivalentTo("2024q1_form13f.zip", "2024q2_form13f.zip", "2024q3_form13f.zip");
    }
}
