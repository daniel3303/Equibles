using System.Globalization;
using System.IO.Compression;
using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Core.Contracts;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Contract for resuming a bulk data set import: every accession a bulk import commits is
/// recorded in its own flush transaction, a later import of the same archive at the same parser
/// version leaves those accessions as stored and flushes the rest in the usual pass order, and a
/// changed archive starts the archive over.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HoldingsImportResumeTests : IAsyncLifetime
{
    private const string DataSet = "2024q4_form13f.zip";
    private const string Cover =
        "ACCESSION_NUMBER\tISAMENDMENT\tAMENDMENTTYPE\tFILINGMANAGER_NAME\n";
    private const string InfoHeader =
        "ACCESSION_NUMBER\tCUSIP\tSSHPRNAMT\tSSHPRNAMTTYPE\tPUTCALL\tINVESTMENTDISCRETION\tVOTING_AUTH_SOLE\tVOTING_AUTH_SHARED\tVOTING_AUTH_NONE\tTITLEOFCLASS\tOTHERMANAGER\n";
    private const string Submissions =
        "SUBMISSIONTYPE\tACCESSION_NUMBER\tFILING_DATE\tPERIODOFREPORT\tCIK\n";
    private const string OriginalSubmission =
        "13F-HR\tACC-ORIG\t2024-10-15\t2024-09-30\t0001067983\n";
    private const string OriginalRows =
        "ACC-ORIG\t037833100\t1000\tSH\t\tSOLE\t1000\t0\t0\tCOM\t\n"
        + "ACC-ORIG\t594918104\t500\tSH\t\tSOLE\t500\t0\t0\tCOM\t\n";

    private static readonly DateOnly ReportDate = new(2024, 9, 30);

    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];
    private readonly CultureInfo _previousCulture;
    private EquityIssuer _apple;
    private EquityIssuer _microsoft;

    public HoldingsImportResumeTests(ParadeDbFixture fixture)
    {
        _fixture = fixture;
        _previousCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetAsync();
        _apple = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "AAPL",
            Name: "Apple Inc",
            Cik: "0000320193",
            Cusip: "037833100"
        );
        _microsoft = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "MSFT",
            Name: "Microsoft Corp",
            Cik: "0000789019",
            Cusip: "594918104"
        );
        await using var seed = _fixture.CreateDbContext();
        seed.Set<EquityIssuer>().AddRange(_apple, _microsoft);
        await seed.SaveChangesAsync();
    }

    public Task DisposeAsync()
    {
        foreach (var ctx in _contexts)
            ctx.Dispose();
        CultureInfo.CurrentCulture = _previousCulture;
        return Task.CompletedTask;
    }

    [Fact]
    public async Task ImportDataSet_RecordsEveryAccessionABulkImportCommits()
    {
        using var archive = OriginalWithNewHoldingsAmendment();

        await Importer().ImportDataSet(archive, Since, TimeSpan.Zero, DataSet, default);

        await using var read = _fixture.CreateDbContext();
        var rows = await read.Set<ProcessedDataSetAccession>().ToListAsync();
        rows.Select(row => row.AccessionNumber).Should().BeEquivalentTo(["ACC-ORIG", "ACC-AMEND"]);
        rows.Should()
            .OnlyContain(row =>
                row.FileName == DataSet
                && row.ParserVersion == ProcessedDataSet.CurrentParserVersion
                && row.ArchiveFingerprint == HoldingsImportService.ArchiveFingerprint(archive)
            );
    }

    [Fact]
    public async Task ImportDataSet_WithoutADataSetNameRecordsNothing()
    {
        using var archive = OriginalWithNewHoldingsAmendment();

        await Importer().ImportDataSet(archive, Since, default);

        await using var read = _fixture.CreateDbContext();
        (await read.Set<ProcessedDataSetAccession>().AnyAsync())
            .Should()
            .BeFalse("only the bulk walk resumes; realtime filings have their own ledger");
    }

    [Fact]
    public async Task ImportDataSet_ResumesAfterTheAccessionsAnInterruptedImportCommitted()
    {
        using var archive = OriginalWithNewHoldingsAmendment();
        await StoreOriginalAsCommitted(HoldingsImportService.ArchiveFingerprint(archive));

        await Importer().ImportDataSet(archive, Since, TimeSpan.Zero, DataSet, default);

        await using var read = _fixture.CreateDbContext();
        var holdings = await read.Set<InstitutionalHolding>().ToListAsync();
        holdings
            .Should()
            .ContainSingle(h => h.EquityIssuerId == _microsoft.Id)
            .Which.Shares.Should()
            .Be(777, "the committed original is left as stored, not flushed again");
        holdings
            .Should()
            .ContainSingle(h => h.EquityIssuerId == _apple.Id)
            .Which.Should()
            .Match<InstitutionalHolding>(h => h.AccessionNumber == "ACC-AMEND" && h.Shares == 1500);
        (
            await read.Set<ProcessedDataSetAccession>()
                .Select(row => row.AccessionNumber)
                .ToListAsync()
        )
            .Should()
            .BeEquivalentTo(["ACC-ORIG", "ACC-AMEND"]);
    }

    [Fact]
    public async Task ImportDataSet_StartsOverWhenTheArchiveChanged()
    {
        using var archive = OriginalWithNewHoldingsAmendment();
        await StoreOriginalAsCommitted("an earlier publication of the archive");

        await Importer().ImportDataSet(archive, Since, TimeSpan.Zero, DataSet, default);

        await using var read = _fixture.CreateDbContext();
        (await read.Set<InstitutionalHolding>().SingleAsync(h => h.EquityIssuerId == _microsoft.Id))
            .Shares.Should()
            .Be(500, "a checkpoint of another archive says nothing about this one");
        (
            await read.Set<ProcessedDataSetAccession>()
                .Select(row => row.ArchiveFingerprint)
                .ToListAsync()
        )
            .Should()
            .AllBe(HoldingsImportService.ArchiveFingerprint(archive));
    }

    private static DateOnly Since => new(2024, 1, 1);

    // The original's rows as its own flush writes them, with the MSFT count changed to 777 so a
    // second flush of the original would show, and a checkpoint row naming it committed.
    private async Task StoreOriginalAsCommitted(string fingerprint)
    {
        using (
            var original = Archive(
                Submissions + OriginalSubmission,
                Cover + "ACC-ORIG\tN\t\tBig Fund\n",
                InfoHeader + OriginalRows
            )
        )
            await Importer().ImportDataSet(original, Since, default);
        await using var seed = _fixture.CreateDbContext();
        await seed.Set<InstitutionalHolding>()
            .Where(h => h.EquityIssuerId == _microsoft.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(h => h.Shares, 777));
        seed.Set<ProcessedDataSetAccession>().Add(Committed(DataSet, "ACC-ORIG", fingerprint));
        await seed.SaveChangesAsync();
    }

    // Two passes for one book: the original, then a NEW HOLDINGS amendment that re-files AAPL.
    private static ZipArchive OriginalWithNewHoldingsAmendment() =>
        Archive(
            Submissions
                + OriginalSubmission
                + "13F-HR/A\tACC-AMEND\t2024-11-20\t2024-09-30\t0001067983\n",
            Cover + "ACC-ORIG\tN\t\tBig Fund\n" + "ACC-AMEND\tY\tNEW HOLDINGS\tBig Fund\n",
            InfoHeader
                + OriginalRows
                + "ACC-AMEND\t037833100\t1500\tSH\t\tSOLE\t1500\t0\t0\tCOM\t\n"
        );

    private static ProcessedDataSetAccession Committed(
        string fileName,
        string accession,
        string fingerprint
    ) =>
        new()
        {
            FileName = fileName,
            AccessionNumber = accession,
            ParserVersion = ProcessedDataSet.CurrentParserVersion,
            ArchiveFingerprint = fingerprint,
        };

    private static ZipArchive Archive(string submissions, string cover, string info)
    {
        var buffer = new MemoryStream();
        using (var writer = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (
                var (name, body) in new[]
                {
                    ("SUBMISSION.tsv", submissions),
                    ("COVERPAGE.tsv", cover),
                    ("INFOTABLE.tsv", info),
                }
            )
            {
                using var stream = writer.CreateEntry(name).Open();
                stream.Write(Encoding.UTF8.GetBytes(body));
            }
        }
        buffer.Position = 0;
        return new ZipArchive(buffer, ZipArchiveMode.Read);
    }

    private HoldingsImportService Importer()
    {
        var prices = Substitute.For<IStockPriceProvider>();
        prices
            .GetClosingPrices(
                Arg.Any<IEnumerable<(Guid, string, DateOnly)>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Task.FromResult(
                    new Dictionary<(Guid, string, DateOnly), decimal>
                    {
                        [(_apple.Id, null, ReportDate)] = 100m,
                        [(_microsoft.Id, null, ReportDate)] = 100m,
                    }
                )
            );
        return new HoldingsImportService(
            ScopeFactory(),
            Substitute.For<ILogger<HoldingsImportService>>(),
            Options.Create(new WorkerOptions()),
            prices,
            Substitute.For<MassTransit.IBus>()
        );
    }

    private IServiceScopeFactory ScopeFactory()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory
            .CreateScope()
            .Returns(_ =>
            {
                var ctx = _fixture.CreateDbContext();
                _contexts.Add(ctx);
                var provider = Substitute.For<IServiceProvider>();
                provider.GetService(typeof(EquiblesFinancialDbContext)).Returns(ctx);
                provider
                    .GetService(typeof(EquityIssuerRepository))
                    .Returns(new EquityIssuerRepository(ctx));
                provider
                    .GetService(typeof(InstitutionalHolderRepository))
                    .Returns(new InstitutionalHolderRepository(ctx));
                provider
                    .GetService(typeof(InstitutionalHoldingRepository))
                    .Returns(new InstitutionalHoldingRepository(ctx));
                provider
                    .GetService(typeof(ProcessedDataSetRepository))
                    .Returns(new ProcessedDataSetRepository(ctx));
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(provider);
                return scope;
            });
        return scopeFactory;
    }
}
