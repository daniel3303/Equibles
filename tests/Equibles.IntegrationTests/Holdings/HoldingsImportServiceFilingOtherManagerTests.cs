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
/// Pins <c>FlushFilingOtherManagers</c> against a real database: the replace-per-accession
/// contract, the two directions staying distinct, and the two ways the phase can silently destroy
/// data it should not touch.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HoldingsImportServiceFilingOtherManagerTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];
    private readonly CultureInfo _previousCulture;

    public HoldingsImportServiceFilingOtherManagerTests(ParadeDbFixture fixture)
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
                Arg.Any<IEnumerable<(Guid, DateOnly)>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(new Dictionary<(Guid, DateOnly), decimal>()));

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

    private const string Submission =
        "SUBMISSIONTYPE\tACCESSION_NUMBER\tFILING_DATE\tPERIODOFREPORT\tCIK\n"
        + "13F-HR\tACC-001\t2024-10-15\t2024-09-30\t0000886982\n";

    private const string CoverPage =
        "ACCESSION_NUMBER\tISAMENDMENT\tFILINGMANAGER_NAME\tFILINGMANAGER_CITY\t"
        + "FILINGMANAGER_STATEORCOUNTRY\tFORM13FFILENUMBER\tCRDNUMBER\n"
        + "ACC-001\tN\tGOLDMAN SACHS GROUP INC\tNEW YORK\tNY\t028-04981\t\n";

    // The import bails before the flush unless at least one position maps to a tracked stock, so
    // the table carries a real row attributed to the first co-manager.
    private const string InfoTable =
        "ACCESSION_NUMBER\tCUSIP\tSSHPRNAMT\tSSHPRNAMTTYPE\tPUTCALL\tINVESTMENTDISCRETION\t"
        + "VOTING_AUTH_SOLE\tVOTING_AUTH_SHARED\tVOTING_AUTH_NONE\tTITLEOFCLASS\tOTHERMANAGER\n"
        + "ACC-001\t037833100\t1000\tSH\t\tSOLE\t1000\t0\t0\tCOM\t1\n";

    private async Task SeedTrackedStock()
    {
        using var seed = FreshContext();
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

    private const string OtherManager2 =
        "ACCESSION_NUMBER\tSEQUENCENUMBER\tCIK\tFORM13FFILENUMBER\tCRDNUMBER\tSECFILENUMBER\tNAME\n"
        + "ACC-001\t1\t0000769993\t028-00687\t000000361\t\tGOLDMAN SACHS & CO. LLC\n"
        + "ACC-001\t2\t0001229262\t028-10981\t000107738\t\tGOLDMAN SACHS ASSET MANAGEMENT, L.P.\n";

    private const string OtherManagerCover =
        "ACCESSION_NUMBER\tOTHERMANAGER_SK\tCIK\tFORM13FFILENUMBER\tCRDNUMBER\tSECFILENUMBER\tNAME\n"
        + "ACC-001\t7\t0000895421\t028-24289\t\t\tMORGAN STANLEY\n";

    private async Task Import(params (string Name, string Body)[] entries)
    {
        using var archive = BuildArchive(entries);
        await CreateImporter()
            .ImportDataSet(archive, new DateOnly(2024, 1, 1), CancellationToken.None);
    }

    [Fact]
    public async Task ImportDataSet_BothManagerLists_PersistsEachUnderItsOwnDirection()
    {
        await SeedTrackedStock();
        // The whole point of the table: a combination report's subsidiaries become rows carrying
        // the identifiers that make them linkable, and the cover page's opposite edge is stored as
        // a distinct direction rather than merged into the same list.
        await Import(
            ("SUBMISSION.tsv", Submission),
            ("COVERPAGE.tsv", CoverPage),
            ("INFOTABLE.tsv", InfoTable),
            ("OTHERMANAGER2.tsv", OtherManager2),
            ("OTHERMANAGER.tsv", OtherManagerCover)
        );

        using var verify = FreshContext();
        var rows = await verify
            .Set<FilingOtherManager>()
            .OrderBy(m => m.Direction)
            .ThenBy(m => m.SequenceNumber)
            .ToListAsync();

        rows.Should().HaveCount(3);

        rows[0].Direction.Should().Be(OtherManagerDirection.IncludedInReport);
        rows[0].SequenceNumber.Should().Be(1);
        rows[0].Name.Should().Be("GOLDMAN SACHS & CO. LLC");
        rows[0].Cik.Should().Be("769993", "the stored spelling has no leading zeros");
        rows[0].Form13FFileNumber.Should().Be("028-00687");
        rows[0].CrdNumber.Should().Be("000000361");
        rows[0].SecFileNumber.Should().BeNull();

        rows[1].Direction.Should().Be(OtherManagerDirection.IncludedInReport);
        rows[1].SequenceNumber.Should().Be(2);
        rows[1].Cik.Should().Be("1229262");

        // The cover page files no sequence number, so the stored ordinal is positional — the SEC's
        // surrogate key (7) orders the list but is not itself stored.
        rows[2].Direction.Should().Be(OtherManagerDirection.ReportsForFiler);
        rows[2].SequenceNumber.Should().Be(1);
        rows[2].Name.Should().Be("MORGAN STANLEY");
        rows[2].Cik.Should().Be("895421");
    }

    [Fact]
    public async Task ImportDataSet_SameArchiveTwice_DoesNotDuplicateTheManagerRows()
    {
        await SeedTrackedStock();
        // History is re-imported whenever the parser version moves, so the phase runs repeatedly
        // over the same accessions. Appending instead of replacing would multiply every
        // combination report's manager list by the number of re-imports.
        var entries = new[]
        {
            ("SUBMISSION.tsv", Submission),
            ("COVERPAGE.tsv", CoverPage),
            ("INFOTABLE.tsv", InfoTable),
            ("OTHERMANAGER2.tsv", OtherManager2),
            ("OTHERMANAGER.tsv", OtherManagerCover),
        };

        await Import(entries);
        await Import(entries);

        using var verify = FreshContext();
        (await verify.Set<FilingOtherManager>().CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task ImportDataSet_ArchiveNoLongerDeclaringManagers_ClearsTheStaleList()
    {
        await SeedTrackedStock();
        // A restated filing can drop managers it previously named. Deleting only the accessions
        // that produced rows would leave the old list behind for ever, so the delete spans every
        // 13F accession the import covers.
        await Import(
            ("SUBMISSION.tsv", Submission),
            ("COVERPAGE.tsv", CoverPage),
            ("INFOTABLE.tsv", InfoTable),
            ("OTHERMANAGER2.tsv", OtherManager2),
            ("OTHERMANAGER.tsv", OtherManagerCover)
        );

        await Import(
            ("SUBMISSION.tsv", Submission),
            ("COVERPAGE.tsv", CoverPage),
            ("INFOTABLE.tsv", InfoTable)
        );

        using var verify = FreshContext();
        (await verify.Set<FilingOtherManager>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ImportDataSet_ScheduleFiling_LeavesAnotherFilingsManagersAlone()
    {
        await SeedTrackedStock();
        // Schedule 13D/G shares this pipeline and its synthetic archive ships a header-only
        // OTHERMANAGER2 section. Without the Form 13F filter the phase would treat those
        // accessions as "covered", find no rows, and delete manager lists it never owned.
        await Import(
            ("SUBMISSION.tsv", Submission),
            ("COVERPAGE.tsv", CoverPage),
            ("INFOTABLE.tsv", InfoTable),
            ("OTHERMANAGER2.tsv", OtherManager2)
        );

        var scheduleSubmission =
            "SUBMISSIONTYPE\tACCESSION_NUMBER\tFILING_DATE\tPERIODOFREPORT\tCIK\n"
            + "SC 13G\tACC-001\t2024-10-16\t2024-09-30\t0000886982\n";
        var scheduleCover =
            "ACCESSION_NUMBER\tISAMENDMENT\tFILINGMANAGER_NAME\tFILINGMANAGER_CITY\t"
            + "FILINGMANAGER_STATEORCOUNTRY\tFORM13FFILENUMBER\tCRDNUMBER\n"
            + "ACC-001\tN\tGOLDMAN SACHS GROUP INC\tNEW YORK\tNY\t\t\n";

        await Import(
            ("SUBMISSION.tsv", scheduleSubmission),
            ("COVERPAGE.tsv", scheduleCover),
            ("INFOTABLE.tsv", InfoTable),
            (
                "OTHERMANAGER2.tsv",
                "ACCESSION_NUMBER\tSEQUENCENUMBER\tCIK\tFORM13FFILENUMBER\tCRDNUMBER\t"
                    + "SECFILENUMBER\tNAME\n"
            )
        );

        using var verify = FreshContext();
        (await verify.Set<FilingOtherManager>().CountAsync())
            .Should()
            .Be(2, "a Schedule 13D/G import must not touch a 13F filing's manager list");
    }

    [Fact]
    public async Task ImportDataSet_LegacyThreeColumnSection_StillStoresTheNames()
    {
        await SeedTrackedStock();
        // Archives predating the identifier columns must keep parsing. The managers are stored
        // with null identifiers — displayable, not linkable — rather than being dropped.
        await Import(
            ("SUBMISSION.tsv", Submission),
            ("COVERPAGE.tsv", CoverPage),
            ("INFOTABLE.tsv", InfoTable),
            (
                "OTHERMANAGER2.tsv",
                "ACCESSION_NUMBER\tSEQUENCENUMBER\tNAME\n" + "ACC-001\t1\tLEGACY ADVISORS\n"
            )
        );

        using var verify = FreshContext();
        var row = await verify.Set<FilingOtherManager>().SingleAsync();
        row.Name.Should().Be("LEGACY ADVISORS");
        row.Cik.Should().BeNull();
        row.Form13FFileNumber.Should().BeNull();
    }
}
