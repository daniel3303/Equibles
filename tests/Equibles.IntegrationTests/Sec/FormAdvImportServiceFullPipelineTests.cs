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
/// fixture: a mocked SEC catalogue and downloads return zipped rosters, and the importer must
/// unzip, parse and upsert advisers keyed by CRD with their client types, record each snapshot in
/// the import ledger, and skip a snapshot the current parser already imported.
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
                sp.GetService(typeof(FormAdvRosterImportRepository))
                    .Returns(new FormAdvRosterImportRepository(ctx));
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });
        return scopeFactory;
    }

    private const string RegisteredUrl = "https://www.sec.gov/files/published-registered_1.zip";
    private const string ExemptUrl = "https://www.sec.gov/files/published-exempt.zip";

    private FormAdvImportService CreateSut(
        string csv,
        ISecEdgarClient secEdgarClient = null,
        string exemptCsv = ExemptCsv
    )
    {
        secEdgarClient ??= Substitute.For<ISecEdgarClient>();
        var catalogue = Encoding.UTF8.GetBytes(
            "<a href='/files/published-registered_1.zip'>Registered Investment Advisers, September 2026</a>"
                + "<a href='/files/published-exempt.zip'>Exempt Investment Advisers, September 2026</a>"
        );
        secEdgarClient
            .DownloadStream(FormAdvSnapshotCatalog.PageUrl)
            .Returns(_ => Task.FromResult<Stream>(new MemoryStream(catalogue)));
        secEdgarClient
            .DownloadStream(RegisteredUrl)
            .Returns(_ => Task.FromResult<Stream>(BuildZipStream(csv)));
        secEdgarClient
            .DownloadStream(ExemptUrl)
            .Returns(_ => Task.FromResult<Stream>(BuildZipStream(exemptCsv)));

        return CreateService(secEdgarClient);
    }

    private FormAdvImportService CreateService(ISecEdgarClient secEdgarClient)
    {
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
    public async Task Import_BothRostersPublished_StoresRosterDetailClientTypesAndLedger()
    {
        await CreateSut(SampleCsv).Import(CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();
        var registered = await verify.Set<FormAdvAdviser>().SingleAsync(a => a.Crd == 111);
        registered.Registration.Should().Be(FormAdvRegistration.Registered);
        registered.Cik.Should().Be("102909", "CIKs are stored without leading zeros");
        registered.Lei.Should().Be("5493002789CX3L0CJP65");
        registered.LatestFilingDate.Should().Be(new DateOnly(2026, 9, 21));
        registered.OrganizationForm.Should().Be(FormAdvOrganizationForm.Corporation);
        registered
            .RegistrationBasis.Should()
            .Be(
                FormAdvRegistrationBasis.LargeAdvisoryFirm
                    | FormAdvRegistrationBasis.RegisteredInvestmentCompanyAdviser
            );
        registered.RelatedPersons.Should().Be(FormAdvRelatedPerson.InsuranceCompanyOrAgency);
        registered.AdvisesPrivateFunds.Should().BeTrue();
        registered.HedgeFundCount.Should().Be(2);
        registered.PrivateFundGrossAssets.Should().Be(5_000_000L);

        var clientTypes = await verify
            .Set<FormAdvAdviserClientType>()
            .Where(c => c.Crd == 111)
            .OrderBy(c => c.ClientType)
            .ToListAsync();
        clientTypes
            .Select(c => c.ClientType)
            .Should()
            .Equal(FormAdvClientType.Individuals, FormAdvClientType.InvestmentCompanies);
        clientTypes[0].FewerThanFiveClients.Should().BeTrue();
        clientTypes[0].ClientCount.Should().BeNull();
        clientTypes[1].ClientCount.Should().Be(12);
        clientTypes[1].RegulatoryAum.Should().Be(1_000_000L);
        clientTypes.Should().OnlyContain(c => c.ReportDate == new DateOnly(2026, 9, 1));

        var exempt = await verify.Set<FormAdvAdviser>().SingleAsync(a => a.Crd == 222);
        exempt.Registration.Should().Be(FormAdvRegistration.ExemptReporting);
        exempt.ExemptionBasis.Should().Be(FormAdvExemptionBasis.VentureCapitalFundAdviser);
        exempt.VentureCapitalFundCount.Should().Be(7);
        exempt.Lei.Should().BeNull("\"N/A\" fails the ISO 17442 check");
        exempt.TotalRegulatoryAum.Should().BeNull();

        var ledger = await verify
            .Set<FormAdvRosterImport>()
            .OrderBy(i => i.Registration)
            .ToListAsync();
        ledger
            .Select(i => i.Registration)
            .Should()
            .Equal(FormAdvRegistration.Registered, FormAdvRegistration.ExemptReporting);
        ledger
            .Should()
            .OnlyContain(i =>
                i.ReportDate == new DateOnly(2026, 9, 1)
                && i.ParserVersion == FormAdvImportService.ParserVersion
                && i.AdviserCount == 1
            );
    }

    [Fact]
    public async Task Import_SnapshotsAlreadyImportedByThisParser_DoesNotDownloadArchives()
    {
        await SeedLedger(FormAdvRegistration.Registered, FormAdvImportService.ParserVersion);
        await SeedLedger(FormAdvRegistration.ExemptReporting, FormAdvImportService.ParserVersion);
        var client = Substitute.For<ISecEdgarClient>();

        await CreateSut(SampleCsv, client).Import(CancellationToken.None);

        await client.Received(1).DownloadStream(FormAdvSnapshotCatalog.PageUrl);
        await client.DidNotReceive().DownloadStream(RegisteredUrl);
        await client.DidNotReceive().DownloadStream(ExemptUrl);
    }

    [Fact]
    public async Task Import_SnapshotImportedByAnOlderParser_ReimportsItOnce()
    {
        // Advisers stored before the ledger existed carry the current month but none of the
        // roster detail; the version lever re-reads that same snapshot.
        await SeedLedger(FormAdvRegistration.Registered, FormAdvImportService.ParserVersion - 1);
        await SeedLedger(FormAdvRegistration.ExemptReporting, FormAdvImportService.ParserVersion);
        var client = Substitute.For<ISecEdgarClient>();

        await CreateSut(SampleCsv, client).Import(CancellationToken.None);

        await client.Received(1).DownloadStream(RegisteredUrl);
        await client.DidNotReceive().DownloadStream(ExemptUrl);
        await using var verify = _fixture.CreateDbContext();
        (
            await verify
                .Set<FormAdvRosterImport>()
                .SingleAsync(i => i.Registration == FormAdvRegistration.Registered)
        )
            .ParserVersion.Should()
            .Be(FormAdvImportService.ParserVersion);
    }

    [Fact]
    public async Task Import_ClientTypeNoLongerReported_RemovesItsStaleRow()
    {
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<FormAdvAdviserClientType>()
                .Add(
                    new FormAdvAdviserClientType
                    {
                        Crd = 111,
                        ClientType = FormAdvClientType.Corporations,
                        ClientCount = 3,
                        ReportDate = new DateOnly(2026, 8, 1),
                    }
                );
            seed.Set<FormAdvAdviserClientType>()
                .Add(
                    new FormAdvAdviserClientType
                    {
                        Crd = 999,
                        ClientType = FormAdvClientType.Corporations,
                        ClientCount = 3,
                        ReportDate = new DateOnly(2026, 8, 1),
                    }
                );
            await seed.SaveChangesAsync();
        }

        await CreateSut(SampleCsv).Import(CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();
        (
            await verify
                .Set<FormAdvAdviserClientType>()
                .AnyAsync(c => c.Crd == 111 && c.ClientType == FormAdvClientType.Corporations)
        )
            .Should()
            .BeFalse();
        (await verify.Set<FormAdvAdviserClientType>().AnyAsync(c => c.Crd == 999))
            .Should()
            .BeTrue("an adviser absent from this roster keeps its rows");
    }

    [Fact]
    public async Task Import_RowListingTheOtherRoster_IsNotFiledUnderThisOne()
    {
        var csv = SampleCsv.Replace(",Registered,", ",ERA,");

        await CreateSut(csv).Import(CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();
        (await verify.Set<FormAdvAdviser>().AnyAsync(a => a.Crd == 111)).Should().BeFalse();
        (
            await verify
                .Set<FormAdvRosterImport>()
                .AnyAsync(i => i.Registration == FormAdvRegistration.Registered)
        )
            .Should()
            .BeFalse("a roster that imported nothing is retried");
    }

    [Fact]
    public async Task Import_ExemptRosterLaggingTheRegisteredOne_KeepsTheNewerRegisteredRow()
    {
        // SEC published November's exempt roster as a "no data" notice, so it falls back to
        // October, which still lists an adviser November's registered roster already moved.
        var client = Substitute.For<ISecEdgarClient>();
        var catalogue = Encoding.UTF8.GetBytes(
            "<a href='/files/ria-nov.zip'>Registered Investment Advisers, November 2026</a>"
                + "<a href='/files/era-nov.pdf'>Exempt Investment Advisers, November 2026</a>"
                + "<a href='/files/era-oct.zip'>Exempt Investment Advisers, October 2026</a>"
        );
        client
            .DownloadStream(FormAdvSnapshotCatalog.PageUrl)
            .Returns(_ => Task.FromResult<Stream>(new MemoryStream(catalogue)));
        client
            .DownloadStream("https://www.sec.gov/files/ria-nov.zip")
            .Returns(_ => Task.FromResult<Stream>(BuildZipStream(SampleCsv)));
        client
            .DownloadStream("https://www.sec.gov/files/era-oct.zip")
            .Returns(_ =>
                Task.FromResult<Stream>(BuildZipStream(ExemptCsv.Replace("\n222,", "\n111,")))
            );

        await CreateService(client).Import(CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();
        var adviser = await verify.Set<FormAdvAdviser>().SingleAsync(a => a.Crd == 111);
        adviser.Registration.Should().Be(FormAdvRegistration.Registered);
        adviser.ReportDate.Should().Be(new DateOnly(2026, 11, 1));
        adviser.LegalName.Should().Be("ACME ADVISORS");
        adviser.TotalRegulatoryAum.Should().Be(1_000_000L);
        (await verify.Set<FormAdvAdviserClientType>().CountAsync(c => c.Crd == 111))
            .Should()
            .Be(2, "the older roster must not delete the newer roster's client types");
    }

    private async Task SeedLedger(FormAdvRegistration registration, int parserVersion)
    {
        await using var seed = _fixture.CreateDbContext();
        seed.Set<FormAdvRosterImport>()
            .Add(
                new FormAdvRosterImport
                {
                    Registration = registration,
                    ReportDate = new DateOnly(2026, 9, 1),
                    ParserVersion = parserVersion,
                    AdviserCount = 1,
                }
            );
        await seed.SaveChangesAsync();
    }

    private const string SampleCsv =
        "Organization CRD#,SEC#,Firm Type,CIK#,Total number of CIK numbers,Legal Name,"
        + "Primary Business Name,Main Office City,Main Office State,Main Office Country,"
        + "Website Address,SEC Current Status,Latest ADV Filing Date,1P,2A(1),2A(5),3A,5A,"
        + "5D(a)(1),5D(a)(2),5D(a)(3),5D(d)(1),5D(d)(3),5D(m)(1),5D(m)(2),5D(m)(3),"
        + "5F(2)(a),5F(2)(b),5F(2)(c),5E(1),5E(2),5E(3),5E(4),5E(5),5E(6),5E(7),7A(12),7B,"
        + "Count of Private Funds - 7B(1),Any Hedge Funds,Total number of Hedge funds,"
        + "Total Gross Assets of Private Funds\n"
        + "111,801-11111,Registered,0000102909,1,ACME ADVISORS,ACME,LOS ANGELES,CA,United States,"
        + "https://acme.test,Approved,09/21/2026,5493002789CX3L0CJP65,Y,Y,Corporation,42,"
        + "0,Fewer than 5 clients,,12,\"1,000,000.00\",0,,.00,"
        + "400000.00,600000.00,1000000.00,Y,N,N,N,N,Y,N,Y,Y,2,Y,2,\"5,000,000.00\"\n";

    private const string ExemptCsv =
        "Organization CRD#,SEC#,Firm Type,CIK#,Legal Name,Latest ADV Filing Date,1P,2B(1),"
        + "3A,7B,Count of Private Funds - 7B(1),Any VC Funds,Total number of VC funds,"
        + "Total Gross Assets of Private Funds\n"
        + "222,802-22222,ERA,1949229,VENTURE PARTNERS LLC,03/27/2026,N/A,Y,"
        + "Limited Liability Company,Y,7,Y,7,\"226,090,000.00\"\n";

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
