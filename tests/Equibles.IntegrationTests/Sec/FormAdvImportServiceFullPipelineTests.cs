using System.IO.Compression;
using System.Text;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Sec.Contracts;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// End-to-end pipeline test for <see cref="FormAdvImportService"/> against the shared ParadeDB
/// fixture: a mocked SEC download returns a zipped CSV, and the importer must unzip, parse, and
/// upsert advisers keyed by CRD. The DB-touching phases — the latest-snapshot lookup that drives
/// the up-to-date guard and the per-batch UpsertRange with its On(Crd)/WhenMatched update — are
/// not reachable from the unit tests, so a regression in the import wiring would silently drop
/// adviser data on every worker tick.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FormAdvImportServiceFullPipelineTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];

    public FormAdvImportServiceFullPipelineTests(ParadeDbFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync() => await _fixture.ResetAsync();

    public Task DisposeAsync()
    {
        foreach (var ctx in _contexts)
        {
            ctx.Dispose();
        }
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
                sp.GetService(typeof(FormAdvAdviserRepository))
                    .Returns(new FormAdvAdviserRepository(ctx));
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });
        return scopeFactory;
    }

    private FormAdvImportService CreateSut(string csv, ISecEdgarClient secEdgarClient = null)
    {
        secEdgarClient ??= Substitute.For<ISecEdgarClient>();
        var catalogue = Encoding.UTF8.GetBytes(
            "<a href='/files/published-registered_1.zip'>Registered Investment Advisers, September 2026</a>"
        );
        secEdgarClient
            .DownloadStream(FormAdvSnapshotCatalog.PageUrl)
            .Returns(_ => Task.FromResult<Stream>(new MemoryStream(catalogue)));
        secEdgarClient
            .DownloadStream("https://www.sec.gov/files/published-registered_1.zip")
            .Returns(_ => Task.FromResult<Stream>(BuildZipStream(csv)));

        return new FormAdvImportService(
            CreateScopeFactory(),
            secEdgarClient,
            Substitute.For<ILogger<FormAdvImportService>>(),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );
    }

    [Fact]
    public async Task Import_DownloadsNewestSnapshot_UpsertsAdvisersWithMappedFields()
    {
        await CreateSut(SampleCsv).Import(CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();
        var adviser = await verify.Set<FormAdvAdviser>().SingleOrDefaultAsync(a => a.Crd == 111);

        adviser
            .Should()
            .NotBeNull("the adviser row should be inserted through the UpsertRange INSERT path");
        adviser!.LegalName.Should().Be("ACME ADVISORS");
        adviser.MainOfficeState.Should().Be("CA");
        adviser.NumberOfEmployees.Should().Be(42);
        adviser.TotalRegulatoryAum.Should().Be(1_000_000L);
        adviser.ChargesPercentageOfAum.Should().BeTrue();
        adviser.ChargesPerformanceBased.Should().BeTrue();
        adviser.ReportDate.Should().Be(new DateOnly(2026, 9, 1));
    }

    [Fact]
    public async Task Import_WhenSnapshotNewerThanStored_UpdatesExistingAdviserByCrd()
    {
        // Pre-seed CRD 111 from an older snapshot so the importer's up-to-date guard lets the
        // newer (this-month) file through and the On(Crd)/WhenMatched UPDATE path runs.
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<FormAdvAdviser>()
                .Add(
                    new FormAdvAdviser
                    {
                        Crd = 111,
                        LegalName = "STALE NAME",
                        TotalRegulatoryAum = 1L,
                        ReportDate = new DateOnly(2000, 1, 1),
                    }
                );
            await seed.SaveChangesAsync();
        }

        await CreateSut(SampleCsv).Import(CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();
        var advisers = await verify.Set<FormAdvAdviser>().Where(a => a.Crd == 111).ToListAsync();

        advisers
            .Should()
            .HaveCount(1, "upsert must update the existing row rather than duplicate it");
        advisers[0].LegalName.Should().Be("ACME ADVISORS");
        advisers[0].TotalRegulatoryAum.Should().Be(1_000_000L);
    }

    [Fact]
    public async Task Import_PublishedSnapshotAlreadyStored_DoesNotDownloadArchive()
    {
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<FormAdvAdviser>()
                .Add(
                    new FormAdvAdviser
                    {
                        Crd = 111,
                        LegalName = "CURRENT ADVISER",
                        ReportDate = new DateOnly(2026, 9, 1),
                    }
                );
            await seed.SaveChangesAsync();
        }
        var client = Substitute.For<ISecEdgarClient>();

        await CreateSut(SampleCsv, client).Import(CancellationToken.None);

        await client.Received(1).DownloadStream(FormAdvSnapshotCatalog.PageUrl);
        await client
            .DidNotReceive()
            .DownloadStream("https://www.sec.gov/files/published-registered_1.zip");
    }

    private const string SampleCsv =
        "Organization CRD#,SEC#,Legal Name,Primary Business Name,Main Office City,Main Office State,"
        + "Main Office Country,Website Address,SEC Current Status,5A,5F(2)(a),5F(2)(b),5F(2)(c),"
        + "5E(1),5E(2),5E(3),5E(4),5E(5),5E(6),5E(7)\n"
        + "111,801-11111,ACME ADVISORS,ACME,LOS ANGELES,CA,United States,https://acme.test,Approved,"
        + "42,400000.00,600000.00,1000000.00,Y,N,N,N,N,Y,N\n";

    private static Stream BuildZipStream(string csvBody)
    {
        var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("ia010125.csv");
            using var stream = entry.Open();
            var bytes = Encoding.Latin1.GetBytes(csvBody);
            stream.Write(bytes, 0, bytes.Length);
        }
        buffer.Position = 0;
        return buffer;
    }
}
