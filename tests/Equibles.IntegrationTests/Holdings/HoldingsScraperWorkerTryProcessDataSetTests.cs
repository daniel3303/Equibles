using System.IO.Compression;
using System.Reflection;
using System.Text;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Core.Contracts;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService;
using Equibles.Holdings.HostedService.Services;
using Equibles.Holdings.Repositories;
using Equibles.Integrations.Sec.Contracts;
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
/// The existing Holdings worker tests only pin <c>TryProcessDataSet</c>'s
/// non-transient failure arm (an unresolvable dependency is reported and
/// skipped). This pins the success path end-to-end through the real
/// <see cref="HoldingsDataSetClient"/> + <see cref="HoldingsImportService"/>
/// against the ParadeDB harness: a downloaded archive is parsed and, depending
/// on whether the import completed, the data set is either marked processed
/// (<c>IsComplete</c>) or left for a later cycle (the structural-incomplete
/// branch) — in both cases the scraper reports success so the per-cycle loop
/// advances rather than treating the file as a transient download failure.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HoldingsScraperWorkerTryProcessDataSetTests : ParadeDbMcpTestBase
{
    private const string FileName = "2024q3_form13f.zip";

    public HoldingsScraperWorkerTryProcessDataSetTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private static ErrorReporter BuildErrorReporter() =>
        new(Substitute.For<IServiceScopeFactory>(), Substitute.For<ILogger<ErrorReporter>>());

    private static IConfiguration ConfigWithContactEmail()
    {
        var configuration = Substitute.For<IConfiguration>();
        configuration["Sec:ContactEmail"].Returns("test@example.com");
        return configuration;
    }

    private static byte[] BuildArchiveBytes(params (string Name, string Body)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var writer = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, body) in entries)
            {
                var entry = writer.CreateEntry(name);
                using var stream = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(body);
                stream.Write(bytes, 0, bytes.Length);
            }
        }
        return buffer.ToArray();
    }

    // Real HoldingsDataSetClient over a stubbed ISecEdgarClient that streams the
    // in-memory ZIP — exercises DownloadDataSet (copy → ZipArchive) for real.
    private static HoldingsDataSetClient BuildDataSetClient(byte[] zipBytes)
    {
        var secEdgar = Substitute.For<ISecEdgarClient>();
        secEdgar
            .DownloadStream(Arg.Any<string>())
            .Returns(_ => Task.FromResult<Stream>(new MemoryStream(zipBytes)));
        return new HoldingsDataSetClient(
            secEdgar,
            Substitute.For<ILogger<HoldingsDataSetClient>>()
        );
    }

    // HoldingsImportService resolves its repositories from its own injected
    // scope factory; mirror the wiring the HoldingsImportService tests use so
    // the importer runs against the same ParadeDB context as the worker.
    private HoldingsImportService BuildImporter()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory
            .CreateScope()
            .Returns(_ =>
            {
                var sp = Substitute.For<IServiceProvider>();
                sp.GetService(typeof(CommonStockRepository))
                    .Returns(new CommonStockRepository(DbContext));
                sp.GetService(typeof(InstitutionalHolderRepository))
                    .Returns(new InstitutionalHolderRepository(DbContext));
                sp.GetService(typeof(InstitutionalHoldingRepository))
                    .Returns(new InstitutionalHoldingRepository(DbContext));
                sp.GetService(typeof(EquiblesDbContext)).Returns(DbContext);
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });

        return new HoldingsImportService(
            scopeFactory,
            Substitute.For<ILogger<HoldingsImportService>>(),
            Options.Create(new WorkerOptions()),
            Substitute.For<IStockPriceProvider>()
        );
    }

    private HoldingsScraperWorker BuildWorker(byte[] zipBytes)
    {
        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(ProcessedDataSetRepository), new ProcessedDataSetRepository(DbContext)),
            (typeof(HoldingsDataSetClient), BuildDataSetClient(zipBytes)),
            (typeof(HoldingsImportService), BuildImporter())
        );

        return new HoldingsScraperWorker(
            Substitute.For<ILogger<HoldingsScraperWorker>>(),
            scopeFactory,
            BuildErrorReporter(),
            Options.Create(new WorkerOptions()),
            ConfigWithContactEmail(),
            new HoldingsRescanSignal()
        );
    }

    private static Task<bool> InvokeTryProcessDataSet(HoldingsScraperWorker worker)
    {
        var method = typeof(HoldingsScraperWorker).GetMethod(
            "TryProcessDataSet",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        return (Task<bool>)
            method.Invoke(worker, [FileName, new DateOnly(2024, 1, 1), CancellationToken.None]);
    }

    [Fact]
    public async Task TryProcessDataSet_ImportComplete_MarksProcessedAndReturnsTrue()
    {
        // SUBMISSION.tsv parses but every row is filtered out (wrong form type,
        // empty accession, period before MinReportDate) → ImportDataSet returns
        // (0, IsComplete:true). The success path must persist a ProcessedDataSet
        // row so the file is skipped on the next cycle, and return true so the
        // worker does not schedule it as a failed retry.
        var tsv =
            "SUBMISSIONTYPE\tACCESSION_NUMBER\tFILING_DATE\tPERIODOFREPORT\tCIK\n"
            + "10-K\tACC-001\t2024-01-15\t2024-09-30\t0001234567\n"
            + "13F-HR\t\t2024-01-15\t2024-09-30\t0001234567\n"
            + "13F-HR\tACC-002\t2024-01-15\t2019-09-30\t0001234567\n";
        var zip = BuildArchiveBytes(("SUBMISSION.tsv", tsv));
        var worker = BuildWorker(zip);

        var result = await InvokeTryProcessDataSet(worker);

        result.Should().BeTrue();
        var processed = await DbContext
            .Set<ProcessedDataSet>()
            .AsNoTracking()
            .SingleAsync(p => p.FileName == FileName, CancellationToken.None);
        processed.SubmissionCount.Should().Be(0);
    }

    [Fact]
    public async Task TryProcessDataSet_ImportIncomplete_DoesNotMarkProcessedButReturnsTrue()
    {
        // SUBMISSION.tsv is absent → ImportDataSet returns (0, IsComplete:false).
        // The structural-incomplete branch must log and leave the file for a
        // later cycle (no ProcessedDataSet row) while still returning true — the
        // download itself succeeded, so this is not a transient failure to retry
        // immediately.
        var zip = BuildArchiveBytes(("UNRELATED.tsv", "header\nbody"));
        var worker = BuildWorker(zip);

        var result = await InvokeTryProcessDataSet(worker);

        result.Should().BeTrue();
        var exists = await DbContext
            .Set<ProcessedDataSet>()
            .AsNoTracking()
            .AnyAsync(p => p.FileName == FileName, CancellationToken.None);
        exists.Should().BeFalse();
    }
}
