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
/// A filing's rows live only under its period of report. An earlier parser read the filer-typed
/// cover-page date, so one accession could be stored under a neighbouring or phantom quarter;
/// re-importing it under its real period must remove those rows, leave other filings at the
/// vacated date alone, and mark that date's snapshot for rebuild.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HoldingsImportServiceReportPeriodOwnershipTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];
    private readonly CultureInfo _previousCulture;

    public HoldingsImportServiceReportPeriodOwnershipTests(ParadeDbFixture fixture)
    {
        _fixture = fixture;
        _previousCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    public async Task InitializeAsync() => await _fixture.ResetAsync();

    public Task DisposeAsync()
    {
        foreach (var ctx in _contexts)
            ctx.Dispose();
        CultureInfo.CurrentCulture = _previousCulture;
        return Task.CompletedTask;
    }

    private EquiblesFinancialDbContext FreshContext()
    {
        var ctx = _fixture.CreateDbContext();
        _contexts.Add(ctx);
        return ctx;
    }

    private IServiceScopeFactory CreateScopeFactory()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory
            .CreateScope()
            .Returns(_ =>
            {
                var ctx = FreshContext();
                var sp = Substitute.For<IServiceProvider>();
                sp.GetService(typeof(EquiblesFinancialDbContext)).Returns(ctx);
                sp.GetService(typeof(EquityIssuerRepository))
                    .Returns(new EquityIssuerRepository(ctx));
                sp.GetService(typeof(InstitutionalHolderRepository))
                    .Returns(new InstitutionalHolderRepository(ctx));
                sp.GetService(typeof(InstitutionalHoldingRepository))
                    .Returns(new InstitutionalHoldingRepository(ctx));
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });
        return scopeFactory;
    }

    private HoldingsImportService CreateImporter(IStockPriceProvider priceProvider) =>
        new(
            CreateScopeFactory(),
            Substitute.For<ILogger<HoldingsImportService>>(),
            Options.Create(new WorkerOptions()),
            priceProvider,
            Substitute.For<MassTransit.IBus>()
        );

    private static ZipArchive BuildArchive(params (string Name, string Body)[] entries)
    {
        var buffer = new MemoryStream();
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
        buffer.Position = 0;
        return new ZipArchive(buffer, ZipArchiveMode.Read);
    }

    private static IStockPriceProvider PriceProviderReturning(
        Dictionary<(Guid, string, DateOnly), decimal> prices
    )
    {
        var provider = Substitute.For<IStockPriceProvider>();
        provider
            .GetClosingPrices(
                Arg.Any<IEnumerable<(Guid, string, DateOnly)>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(prices));
        return provider;
    }

    private const string SubmissionHeader =
        "SUBMISSIONTYPE\tACCESSION_NUMBER\tFILING_DATE\tPERIODOFREPORT\tCIK\n";
    private const string CoverHeader =
        "ACCESSION_NUMBER\tISAMENDMENT\tAMENDMENTTYPE\tFILINGMANAGER_NAME\n";
    private const string InfoHeader =
        "ACCESSION_NUMBER\tCUSIP\tSSHPRNAMT\tSSHPRNAMTTYPE\tPUTCALL\tINVESTMENTDISCRETION\tVOTING_AUTH_SOLE\tVOTING_AUTH_SHARED\tVOTING_AUTH_NONE\tTITLEOFCLASS\tOTHERMANAGER\n";

    [Fact]
    public async Task ImportDataSet_AccessionReimportedUnderItsRealQuarter_RemovesItsRowsFromTheWrongQuarterOnly()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "AAPL",
            Name: "Apple Inc",
            Cik: "0000320193",
            Cusip: "037833100"
        );
        var wrongQuarter = new DateOnly(2020, 3, 31);
        var realQuarter = new DateOnly(2020, 6, 30);
        using (var seed = FreshContext())
        {
            seed.Set<EquityIssuer>().Add(stock);
            await seed.SaveChangesAsync();
        }
        var prices = new Dictionary<(Guid, string, DateOnly), decimal>
        {
            [(stock.Id, null, wrongQuarter)] = 100m,
            [(stock.Id, null, realQuarter)] = 110m,
        };

        // 1) The mis-dated capture of ACC-X beside another filer's genuine Q1 book.
        using (
            var misdated = BuildArchive(
                (
                    "SUBMISSION.tsv",
                    SubmissionHeader
                        + "13F-HR\tACC-X\t2020-08-07\t2020-03-31\t0001550660\n"
                        + "13F-HR\tACC-Y\t2020-05-12\t2020-03-31\t0001632932\n"
                ),
                (
                    "COVERPAGE.tsv",
                    CoverHeader + "ACC-X\tN\t\tMisdated Manager\n" + "ACC-Y\tN\t\tOther Manager\n"
                ),
                (
                    "INFOTABLE.tsv",
                    InfoHeader
                        + "ACC-X\t037833100\t1000\tSH\t\tSOLE\t1000\t0\t0\tCOM\t\n"
                        + "ACC-Y\t037833100\t700\tSH\t\tSOLE\t700\t0\t0\tCOM\t\n"
                )
            )
        )
        {
            var sut = CreateImporter(PriceProviderReturning(prices));
            await sut.ImportDataSet(misdated, new DateOnly(2020, 1, 1), CancellationToken.None);
        }

        // The wrong quarter already has a clean snapshot that must be flagged for rebuild.
        using (var seed = FreshContext())
        {
            seed.Set<AumQuarterlySnapshot>()
                .Add(
                    new AumQuarterlySnapshot
                    {
                        ReportDate = wrongQuarter,
                        TotalValue = 170_000,
                        FilerCount = 2,
                        PositionCount = 2,
                        StockCount = 1,
                        FilingCount = 2,
                        ComputedAt = DateTime.UtcNow,
                        DirtyAt = null,
                    }
                );
            await seed.SaveChangesAsync();
        }

        // 2) ACC-X arrives again under its real period of report.
        using (
            var corrected = BuildArchive(
                (
                    "SUBMISSION.tsv",
                    SubmissionHeader + "13F-HR\tACC-X\t2020-08-07\t2020-06-30\t0001550660\n"
                ),
                ("COVERPAGE.tsv", CoverHeader + "ACC-X\tN\t\tMisdated Manager\n"),
                (
                    "INFOTABLE.tsv",
                    InfoHeader + "ACC-X\t037833100\t2000\tSH\t\tSOLE\t2000\t0\t0\tCOM\t\n"
                )
            )
        )
        {
            var sut = CreateImporter(PriceProviderReturning(prices));
            await sut.ImportDataSet(corrected, new DateOnly(2020, 1, 1), CancellationToken.None);
        }

        using var verify = FreshContext();
        var accX = await verify
            .Set<InstitutionalHolding>()
            .Where(h => h.AccessionNumber == "ACC-X")
            .ToListAsync();
        var accXRow = accX.Should().ContainSingle().Subject;
        accXRow.ReportDate.Should().Be(realQuarter);
        accXRow.Shares.Should().Be(2000);

        var wrongQuarterRows = await verify
            .Set<InstitutionalHolding>()
            .Where(h => h.ReportDate == wrongQuarter)
            .ToListAsync();
        var survivor = wrongQuarterRows.Should().ContainSingle().Subject;
        survivor.AccessionNumber.Should().Be("ACC-Y");
        survivor.Shares.Should().Be(700);

        var rollup = await verify
            .Set<InstitutionalFiling>()
            .SingleAsync(f => f.AccessionNumber == "ACC-X");
        rollup.ReportDate.Should().Be(realQuarter);

        var snapshot = await verify
            .Set<AumQuarterlySnapshot>()
            .SingleAsync(s => s.ReportDate == wrongQuarter);
        snapshot.DirtyAt.Should().NotBeNull();
    }
}
