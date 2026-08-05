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
/// Pins the comma-list OTHERMANAGER attribution end-to-end against a real database: a leg filed
/// under "4,8,11" is credited to manager 4 and stays recognizable as shared. Its predecessor
/// parsed the field with a plain int parse, which nulled every multi-manager attribution —
/// Berkshire lost ~85% of its manager split that way in production.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HoldingsImportServiceSharedManagerAttributionTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];
    private readonly CultureInfo _previousCulture;

    public HoldingsImportServiceSharedManagerAttributionTests(ParadeDbFixture fixture)
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
                sp.GetService(typeof(CommonStockRepository))
                    .Returns(new CommonStockRepository(ctx));
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

    [Fact]
    public async Task ImportDataSet_CommaListAttribution_CreditsTheFirstManagerAndKeepsTheList()
    {
        using (var seed = FreshContext())
        {
            seed.Set<CommonStock>()
                .Add(
                    new CommonStock
                    {
                        Id = Guid.NewGuid(),
                        Ticker = "AAPL",
                        Name = "Apple Inc",
                        Cik = "0000320193",
                        Cusip = "037833100",
                    }
                );
            await seed.SaveChangesAsync();
        }

        var submission =
            "SUBMISSIONTYPE\tACCESSION_NUMBER\tFILING_DATE\tPERIODOFREPORT\tCIK\n"
            + "13F-HR\tACC-001\t2024-10-15\t2024-09-30\t0001067983\n";
        var coverPage =
            "ACCESSION_NUMBER\tISAMENDMENT\tFILINGMANAGER_NAME\tFILINGMANAGER_CITY\t"
            + "FILINGMANAGER_STATEORCOUNTRY\tFORM13FFILENUMBER\tCRDNUMBER\n"
            + "ACC-001\tN\tBERKSHIRE HATHAWAY INC\tOMAHA\tNE\t028-00338\t\n";
        var infoTable =
            "ACCESSION_NUMBER\tCUSIP\tSSHPRNAMT\tSSHPRNAMTTYPE\tPUTCALL\tINVESTMENTDISCRETION\t"
            + "VOTING_AUTH_SOLE\tVOTING_AUTH_SHARED\tVOTING_AUTH_NONE\tTITLEOFCLASS\tOTHERMANAGER\n"
            + "ACC-001\t037833100\t1000\tSH\t\tSOLE\t1000\t0\t0\tCOM\t4,8,11\n"
            + "ACC-001\t037833100\t500\tSH\tPut\tSOLE\t500\t0\t0\tCOM\t2\n";
        var otherManager2 =
            "ACCESSION_NUMBER\tSEQUENCENUMBER\tCIK\tFORM13FFILENUMBER\tCRDNUMBER\tSECFILENUMBER\tNAME\n"
            + "ACC-001\t4\t\t\t\t\tGENERAL RE-NEW ENGLAND ASSET MGMT\n";

        using var archive = BuildArchive(
            ("SUBMISSION.tsv", submission),
            ("COVERPAGE.tsv", coverPage),
            ("INFOTABLE.tsv", infoTable),
            ("OTHERMANAGER2.tsv", otherManager2)
        );
        await CreateImporter()
            .ImportDataSet(archive, new DateOnly(2024, 1, 1), CancellationToken.None);

        using var verify = FreshContext();
        var entries = await verify
            .Set<InstitutionalHolding>()
            .AsNoTracking()
            .SelectMany(h => h.ManagerEntries)
            .OrderBy(e => e.ManagerNumber)
            .ToListAsync();

        entries.Should().HaveCount(2);

        // The single-manager leg: an ordinary reference, nothing shared about it.
        entries[0].ManagerNumber.Should().Be(2);
        entries[0].SharedManagerNumbers.Should().BeNull();

        // The comma-list leg: credited to the FIRST referenced manager — whose name resolves
        // through the other-manager table — with the raw list kept so the leg stays
        // recognizable as jointly managed.
        entries[1].ManagerNumber.Should().Be(4);
        entries[1].ManagerName.Should().Be("GENERAL RE-NEW ENGLAND ASSET MGMT");
        entries[1].SharedManagerNumbers.Should().Be("4,8,11");
    }
}
