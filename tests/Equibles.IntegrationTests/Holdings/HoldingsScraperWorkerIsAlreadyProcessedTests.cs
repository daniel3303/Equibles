using System.Reflection;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Sibling to <see cref="HoldingsScraperWorkerBackfillTests"/>. That pins the
/// first-run seeding contract. This pins <c>IsAlreadyProcessed</c>: it must
/// return true when a row with the exact filename exists AND must NOT false-match
/// on a substring or differently-cased name. A regression that downgraded the EF
/// equality to a Contains/LIKE comparison would treat every subsequent quarter as
/// "already processed" the moment one filename was a substring of another,
/// silently halting the import.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HoldingsScraperWorkerIsAlreadyProcessedTests : ParadeDbMcpTestBase
{
    public HoldingsScraperWorkerIsAlreadyProcessedTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task IsAlreadyProcessed_ExactNamePresentSubstringAbsent_ReturnsTrueForExactFalseForSubstring()
    {
        // Seed two real ProcessedDataSet rows at the current parser version.
        // The substring case probes the exact-match guarantee — "2024q1" is a
        // proper substring of the seeded "2024q1_form13f.zip", so a buggy
        // `Contains`/`StartsWith` regression would also report true on the
        // substring.
        DbContext.Add(
            new ProcessedDataSet
            {
                FileName = "2024q1_form13f.zip",
                ParserVersion = ProcessedDataSet.CurrentParserVersion,
            }
        );
        DbContext.Add(
            new ProcessedDataSet
            {
                FileName = "2024q2_form13f.zip",
                ParserVersion = ProcessedDataSet.CurrentParserVersion,
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var worker = CreateWorker();
        var method = typeof(HoldingsScraperWorker).GetMethod(
            "IsAlreadyProcessed",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        var exactHit = await (Task<bool>)method.Invoke(worker, ["2024q1_form13f.zip"]);
        var substringMiss = await (Task<bool>)method.Invoke(worker, ["2024q1"]);
        var unrelated = await (Task<bool>)method.Invoke(worker, ["2024q3_form13f.zip"]);

        exactHit.Should().BeTrue();
        substringMiss.Should().BeFalse();
        unrelated.Should().BeFalse();
    }

    // The self-heal contract behind a parser-version bump: a data set imported
    // by an older parser (including pre-versioning rows, which default to 0)
    // must read as NOT processed so the next cycle re-imports it through the
    // current pipeline. A regression back to a bare existence check would
    // permanently strand stale parses (the #1535 corruption class).
    [Fact]
    public async Task IsAlreadyProcessed_RowStampedBelowCurrentParserVersion_ReturnsFalse()
    {
        DbContext.Add(
            new ProcessedDataSet
            {
                FileName = "2023q4_form13f.zip",
                ParserVersion = ProcessedDataSet.CurrentParserVersion - 1,
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var worker = CreateWorker();
        var method = typeof(HoldingsScraperWorker).GetMethod(
            "IsAlreadyProcessed",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        var staleVersionHit = await (Task<bool>)method.Invoke(worker, ["2023q4_form13f.zip"]);

        staleVersionHit.Should().BeFalse();
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
