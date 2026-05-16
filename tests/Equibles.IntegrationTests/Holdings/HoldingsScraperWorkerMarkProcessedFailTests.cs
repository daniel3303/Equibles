using System.IO.Compression;
using System.Reflection;
using System.Text;
using Equibles.Core.Configuration;
using Equibles.Core.Contracts;
using Equibles.Errors.BusinessLogic;
using Equibles.Holdings.HostedService;
using Equibles.Holdings.HostedService.Services;
using Equibles.Holdings.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.IntegrationTests.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Pins <c>TryProcessDataSet</c>'s MarkAsProcessed-catch: when the import
/// completes but persisting the ProcessedDataSet marker fails, the worker must
/// log the failure (import already succeeded) and still return true — never
/// rethrow. The marker repository is given a disposed context so SaveChanges
/// throws.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HoldingsScraperWorkerMarkProcessedFailTests : ParadeDbMcpTestBase
{
    private const string FileName = "2024q3_form13f.zip";

    public HoldingsScraperWorkerMarkProcessedFailTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private static byte[] BuildArchiveBytes(string submissionTsv)
    {
        using var buffer = new MemoryStream();
        using (var writer = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = writer.CreateEntry("SUBMISSION.tsv");
            using var s = entry.Open();
            var bytes = Encoding.UTF8.GetBytes(submissionTsv);
            s.Write(bytes, 0, bytes.Length);
        }
        return buffer.ToArray();
    }

    private HoldingsDataSetClient BuildDataSetClient(byte[] zip)
    {
        var secEdgar = Substitute.For<ISecEdgarClient>();
        secEdgar
            .DownloadStream(Arg.Any<string>())
            .Returns(_ => Task.FromResult<Stream>(new MemoryStream(zip)));
        return new HoldingsDataSetClient(
            secEdgar,
            Substitute.For<ILogger<HoldingsDataSetClient>>()
        );
    }

    private HoldingsImportService BuildImporter()
    {
        var sf = Substitute.For<IServiceScopeFactory>();
        sf.CreateScope()
            .Returns(_ =>
            {
                var sp = Substitute.For<IServiceProvider>();
                sp.GetService(typeof(Equibles.Data.EquiblesDbContext)).Returns(DbContext);
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });
        return new HoldingsImportService(
            sf,
            Substitute.For<ILogger<HoldingsImportService>>(),
            Options.Create(new WorkerOptions()),
            Substitute.For<IStockPriceProvider>()
        );
    }

    [Fact]
    public async Task TryProcessDataSet_ImportCompleteButMarkProcessedThrows_LogsAndReturnsTrue()
    {
        // SUBMISSION rows all filtered out → ImportDataSet returns
        // (0, IsComplete:true), so the success path calls MarkAsProcessed.
        var tsv =
            "SUBMISSIONTYPE\tACCESSION_NUMBER\tFILING_DATE\tPERIODOFREPORT\tCIK\n"
            + "10-K\tACC-001\t2024-01-15\t2024-09-30\t0001234567\n"
            + "13F-HR\t\t2024-01-15\t2024-09-30\t0001234567\n";
        var zip = BuildArchiveBytes(tsv);

        // The marker repository's context is disposed → SaveChanges throws
        // inside MarkAsProcessed, exercising its catch.
        var disposedCtx = Fixture.CreateDbContext();
        disposedCtx.Dispose();

        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(ProcessedDataSetRepository), new ProcessedDataSetRepository(disposedCtx)),
            (typeof(HoldingsDataSetClient), BuildDataSetClient(zip)),
            (typeof(HoldingsImportService), BuildImporter())
        );

        var config = Substitute.For<IConfiguration>();
        config["Sec:ContactEmail"].Returns("test@example.com");

        var worker = new HoldingsScraperWorker(
            Substitute.For<ILogger<HoldingsScraperWorker>>(),
            scopeFactory,
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new WorkerOptions()),
            config
        );

        var method = typeof(HoldingsScraperWorker).GetMethod(
            "TryProcessDataSet",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        var result = await (Task<bool>)
            method.Invoke(worker, [FileName, new DateOnly(2024, 1, 1), CancellationToken.None]);

        result.Should().BeTrue("the import succeeded; a failed marker write is logged, not fatal");
    }
}
