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
/// Pins <c>MarkAsProcessed</c>: the post-success step the worker calls after a
/// quarterly ZIP imports cleanly. The row it persists is the cursor that
/// <c>IsAlreadyProcessed</c> consults on the next cycle — both <c>FileName</c>
/// AND <c>SubmissionCount</c> must round-trip through Postgres. A regression that
/// dropped the SubmissionCount assignment would break downstream metrics that
/// reconcile expected-vs-actual submission counts per quarter.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HoldingsScraperWorkerMarkAsProcessedTests : ParadeDbMcpTestBase
{
    public HoldingsScraperWorkerMarkAsProcessedTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task MarkAsProcessed_NewFileName_PersistsRowWithSubmissionCountAndCurrentParserVersion()
    {
        var worker = CreateWorker();

        var method = typeof(HoldingsScraperWorker).GetMethod(
            "MarkAsProcessed",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        await (Task)method.Invoke(worker, ["2024q4_form13f.zip", 7842]);

        // Re-read from a fresh context — exercises actual persistence, not tracker state.
        await using var verify = Fixture.CreateDbContext();
        var row = await verify
            .Set<ProcessedDataSet>()
            .AsNoTracking()
            .SingleAsync(p => p.FileName == "2024q4_form13f.zip");
        row.SubmissionCount.Should().Be(7842);
        row.ParserVersion.Should().Be(ProcessedDataSet.CurrentParserVersion);
    }

    // After a parser-version bump the row already exists at the old version
    // (FileName is unique). MarkAsProcessed must update it in place — a blind
    // insert would violate the unique index, the marker would never advance,
    // and the data set would re-import on every single cycle.
    [Fact]
    public async Task MarkAsProcessed_RowExistsAtOlderParserVersion_UpdatesInPlaceWithoutDuplicating()
    {
        DbContext.Add(
            new ProcessedDataSet
            {
                FileName = "2023q2_form13f.zip",
                SubmissionCount = 6500,
                ParserVersion = ProcessedDataSet.CurrentParserVersion - 1,
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var worker = CreateWorker();

        var method = typeof(HoldingsScraperWorker).GetMethod(
            "MarkAsProcessed",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        await (Task)method.Invoke(worker, ["2023q2_form13f.zip", 6612]);

        await using var verify = Fixture.CreateDbContext();
        var row = await verify
            .Set<ProcessedDataSet>()
            .AsNoTracking()
            .SingleAsync(p => p.FileName == "2023q2_form13f.zip");
        row.SubmissionCount.Should().Be(6612);
        row.ParserVersion.Should().Be(ProcessedDataSet.CurrentParserVersion);
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
