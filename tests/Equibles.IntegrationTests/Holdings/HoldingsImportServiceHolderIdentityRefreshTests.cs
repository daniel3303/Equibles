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
/// Pins the holder identity refresh through the whole import: a holder's 13F file number, CRD and
/// SEC file number follow its newest report in the archive, whatever the TSV row order, and a
/// parent's number the old cover parser stored is dropped when the cover lists it for another
/// manager.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HoldingsImportServiceHolderIdentityRefreshTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];
    private readonly CultureInfo _previousCulture;

    public HoldingsImportServiceHolderIdentityRefreshTests(ParadeDbFixture fixture)
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

    private HoldingsImportService CreateImporter()
    {
        var priceProvider = Substitute.For<IStockPriceProvider>();
        priceProvider
            .GetClosingPrices(
                Arg.Any<IEnumerable<(Guid, string, DateOnly)>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(new Dictionary<(Guid, string, DateOnly), decimal>()));

        return new HoldingsImportService(
            CreateScopeFactory(),
            Substitute.For<ILogger<HoldingsImportService>>(),
            Options.Create(new WorkerOptions()),
            priceProvider,
            Substitute.For<MassTransit.IBus>()
        );
    }

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

    // The import bails before the flush unless at least one position maps to a tracked stock, so
    // the table carries a real row attributed to the first co-manager.

    private async Task SeedTrackedStock()
    {
        using var seed = FreshContext();
        seed.Set<EquityIssuer>()
            .Add(
                Equibles.TestSupport.EquityIssuerSeed.Create(
                    Id: Guid.NewGuid(),
                    Ticker: "AAPL",
                    Name: "Apple Inc",
                    Cik: "0000320193",
                    Cusip: "037833100"
                )
            );
        await seed.SaveChangesAsync();
    }

    private const string InfoTable =
        "ACCESSION_NUMBER\tCUSIP\tSSHPRNAMT\tSSHPRNAMTTYPE\tPUTCALL\tINVESTMENTDISCRETION\t"
        + "VOTING_AUTH_SOLE\tVOTING_AUTH_SHARED\tVOTING_AUTH_NONE\tTITLEOFCLASS\tOTHERMANAGER\n"
        + "ACC-OLD\t037833100\t1000\tSH\t\tSOLE\t1000\t0\t0\tCOM\t\n"
        + "ACC-NEW\t037833100\t1000\tSH\t\tSOLE\t1000\t0\t0\tCOM\t\n";

    private const string CoverHeader =
        "ACCESSION_NUMBER\tISAMENDMENT\tFILINGMANAGER_NAME\tFILINGMANAGER_CITY\t"
        + "FILINGMANAGER_STATEORCOUNTRY\tFORM13FFILENUMBER\tCRDNUMBER\tSECFILENUMBER\n";

    // The older quarter's rows come first in every file, so a refresh keyed on row order would
    // keep the stale numbers.
    private static string Submissions(string cik) =>
        "SUBMISSIONTYPE\tACCESSION_NUMBER\tFILING_DATE\tPERIODOFREPORT\tCIK\n"
        + $"13F-HR\tACC-OLD\t2024-08-14\t2024-06-30\t{cik}\n"
        + $"13F-HR\tACC-NEW\t2024-11-14\t2024-09-30\t{cik}\n";

    private static string Covers(string newCrd) =>
        CoverHeader
        + "ACC-OLD\tN\tVANGUARD GROUP INC\tMALVERN\tPA\t028-00001\t000000111\t801-00001\n"
        + $"ACC-NEW\tN\tVANGUARD GROUP INC\tMALVERN\tPA\t028-06408\t{newCrd}\t801-11953\n";

    [Fact]
    public async Task ExistingHolderUnderAnotherCikSpelling_TakesItsNewestReportsNumbers()
    {
        await SeedTrackedStock();
        var holderId = await SeedHolder("0000102909", crd: null);

        await Import(
            ("SUBMISSION.tsv", Submissions("102909")),
            ("COVERPAGE.tsv", Covers("000105958")),
            ("INFOTABLE.tsv", InfoTable)
        );

        var holder = await Load(holderId);
        holder.CrdNumber.Should().Be("000105958");
        holder.Form13FFileNumber.Should().Be("028-06408");
        holder.SecFileNumber.Should().Be("801-11953");
        holder.IdentityAccession.Should().Be("ACC-NEW");
    }

    [Fact]
    public async Task NewHolder_TakesItsNewestReportsNumbersNotTheFirstRows()
    {
        await SeedTrackedStock();

        await Import(
            ("SUBMISSION.tsv", Submissions("0000102909")),
            ("COVERPAGE.tsv", Covers("000105958")),
            ("INFOTABLE.tsv", InfoTable)
        );

        using var verify = FreshContext();
        var holder = await verify.Set<InstitutionalHolder>().AsNoTracking().SingleAsync();
        holder.CrdNumber.Should().Be("000105958");
        holder.SecFileNumber.Should().Be("801-11953");
        holder.IdentityAccession.Should().Be("ACC-NEW");
    }

    [Fact]
    public async Task StoredNumberTheNewestCoverListsForAnotherManager_IsDropped()
    {
        await SeedTrackedStock();
        var holderId = await SeedHolder("0000895421", crd: "000000361");
        var otherManagerCover =
            "ACCESSION_NUMBER\tOTHERMANAGER_SK\tCIK\tFORM13FFILENUMBER\tCRDNUMBER\tSECFILENUMBER\tNAME\n"
            + "ACC-NEW\t1\t0002026079\t028-24289\t000000361\t\tPARENT REPORTING FOR US\n";

        await Import(
            ("SUBMISSION.tsv", Submissions("0000895421")),
            ("COVERPAGE.tsv", Covers("")),
            ("INFOTABLE.tsv", InfoTable),
            ("OTHERMANAGER.tsv", otherManagerCover)
        );

        (await Load(holderId)).CrdNumber.Should().BeNull();
    }

    private async Task<Guid> SeedHolder(string cik, string crd)
    {
        using var seed = FreshContext();
        var holder = new InstitutionalHolder
        {
            Cik = cik,
            Name = "Seeded",
            CrdNumber = crd,
        };
        seed.Add(holder);
        await seed.SaveChangesAsync();
        return holder.Id;
    }

    private async Task<InstitutionalHolder> Load(Guid holderId)
    {
        using var verify = FreshContext();
        return await verify
            .Set<InstitutionalHolder>()
            .AsNoTracking()
            .SingleAsync(h => h.Id == holderId);
    }

    private async Task Import(params (string Name, string Body)[] entries)
    {
        using var archive = BuildArchive(entries);
        await CreateImporter()
            .ImportDataSet(archive, new DateOnly(2024, 1, 1), CancellationToken.None);
    }
}
