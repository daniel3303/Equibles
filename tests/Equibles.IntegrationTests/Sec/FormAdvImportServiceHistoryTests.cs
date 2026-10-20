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
/// The Form ADV version history against Postgres: the walk folds rosters in strictly oldest
/// first, one per cycle, writes a version only when an adviser's tracked content changes or it
/// returns after missing a roster, and the current-view import joins the history only once the
/// walk has caught up to it.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FormAdvImportServiceHistoryTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];

    public FormAdvImportServiceHistoryTests(ParadeDbFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetAsync();
        await using var seed = _fixture.CreateDbContext();
        foreach (var registration in Enum.GetValues<FormAdvRegistration>())
            seed.Set<FormAdvFilingArchiveImport>()
                .Add(
                    new FormAdvFilingArchiveImport
                    {
                        Registration = registration,
                        ArchiveEndDate = FormAdvPrivateFundArchiveImporter.ArchiveEndDate,
                        ParserVersion = FormAdvPrivateFundArchiveImporter.ParserVersion,
                    }
                );
        await seed.SaveChangesAsync();
    }

    public Task DisposeAsync()
    {
        foreach (var context in _contexts)
            context.Dispose();
        return Task.CompletedTask;
    }

    private static readonly DateOnly December2019 = new(2019, 12, 1);
    private static readonly DateOnly January2020 = new(2020, 1, 1);
    private static readonly DateOnly March2020 = new(2020, 3, 1);
    private static readonly DateOnly April2020 = new(2020, 4, 1);

    [Fact]
    public async Task ImportCycle_WalksRostersOldestFirst_WritingVersionsOnlyOnChange()
    {
        var client = Catalogue(
            ("Registered Investment Advisers, December 2019", "ria-1219.zip", Roster(Acme(1_000))),
            (
                "Registered Investment Advisers, January 2020",
                "ria-0120.zip",
                Roster(Acme(1_000, filedMonth: 12), Beta())
            ),
            ("Registered Investment Advisers, February 2020", "ria-0220.pdf", null),
            ("Registered Investment Advisers, March 2020", "ria-0320.zip", Roster(Acme(2_000))),
            (
                "Registered Investment Advisers, April 2020",
                "ria-0420.zip",
                Roster(Acme(2_000), Beta())
            ),
            ("Exempt Investment Advisers, April 2020", "era-0420.zip", ExemptRoster)
        );
        var sut = CreateService(client);

        var cycles = 0;
        while (await sut.ImportCycle(CancellationToken.None))
            (++cycles).Should().BeLessThan(10);

        await using var verify = _fixture.CreateDbContext();
        var acme = await verify
            .Set<FormAdvAdviserVersion>()
            .Include(version => version.ClientTypes)
            .Where(version => version.Crd == 111)
            .OrderBy(version => version.ReportDate)
            .ToListAsync();
        acme.Select(version => (version.ReportDate, version.LastSeenReportDate))
            .Should()
            .Equal((December2019, January2020), (March2020, April2020));
        acme.Select(version => version.TotalRegulatoryAum).Should().Equal(1_000L, 2_000L);
        acme.Should().OnlyContain(version => version.ClientTypes.Count == 2);

        // Beta left the March roster, so its April listing starts a new version.
        var beta = await verify
            .Set<FormAdvAdviserVersion>()
            .Where(version => version.Crd == 333)
            .OrderBy(version => version.ReportDate)
            .Select(version => new { version.ReportDate, version.LastSeenReportDate })
            .ToListAsync();
        beta.Select(version => (version.ReportDate, version.LastSeenReportDate))
            .Should()
            .Equal((January2020, January2020), (April2020, April2020));

        var ledger = await verify
            .Set<FormAdvRosterImport>()
            .Where(import => import.Registration == FormAdvRegistration.Registered)
            .OrderBy(import => import.ReportDate)
            .ToListAsync();
        ledger
            .Select(import => import.ReportDate)
            .Should()
            .Equal(December2019, January2020, March2020, April2020);
        ledger.Should().OnlyContain(import => import.VersionedAt != null);
        ledger
            .Select(import => import.ParserVersion)
            .Should()
            .Equal(0, 0, 0, FormAdvImportService.ParserVersion);

        var current = await verify.Set<FormAdvAdviser>().SingleAsync(a => a.Crd == 111);
        current.ReportDate.Should().Be(April2020);
        current.LastSeenReportDate.Should().Be(April2020);
        (await verify.Set<FormAdvAdviserVersion>().SingleAsync(version => version.Crd == 222))
            .Registration.Should()
            .Be(FormAdvRegistration.ExemptReporting);

        // A finished walk reads nothing more.
        client.ClearReceivedCalls();
        (await sut.ImportCycle(CancellationToken.None)).Should().BeFalse();
        await client.DidNotReceive().DownloadStream(Arg.Is<string>(url => url.EndsWith(".zip")));
    }

    [Fact]
    public async Task ImportCycle_NewRosterAfterACaughtUpWalk_JoinsTheHistoryWithTheCurrentView()
    {
        var walked = Catalogue(
            ("Registered Investment Advisers, March 2020", "ria-0320.zip", Roster(Acme(2_000))),
            ("Exempt Investment Advisers, March 2020", "era-0320.zip", ExemptRoster)
        );
        var first = CreateService(walked);
        while (await first.ImportCycle(CancellationToken.None)) { }

        var next = Catalogue(
            ("Registered Investment Advisers, April 2020", "ria-0420.zip", Roster(Acme(3_000))),
            ("Registered Investment Advisers, March 2020", "ria-0320.zip", Roster(Acme(2_000))),
            ("Exempt Investment Advisers, March 2020", "era-0320.zip", ExemptRoster)
        );
        var more = await CreateService(next).ImportCycle(CancellationToken.None);

        more.Should().BeFalse("the current-view pass already folded April into the history");
        await next.Received(1).DownloadStream("https://www.sec.gov/files/ria-0420.zip");
        await using var verify = _fixture.CreateDbContext();
        var april = await verify
            .Set<FormAdvRosterImport>()
            .SingleAsync(import =>
                import.Registration == FormAdvRegistration.Registered
                && import.ReportDate == April2020
            );
        april.ParserVersion.Should().Be(FormAdvImportService.ParserVersion);
        april.VersionedAt.Should().NotBeNull();
        (
            await verify
                .Set<FormAdvAdviserVersion>()
                .Where(version => version.Crd == 111)
                .OrderBy(version => version.ReportDate)
                .Select(version => version.TotalRegulatoryAum)
                .ToListAsync()
        )
            .Should()
            .Equal(2_000L, 3_000L);
    }

    [Fact]
    public async Task ImportCycle_RetriedRoster_ChangesNothing()
    {
        var client = Catalogue(
            ("Registered Investment Advisers, March 2020", "ria-0320.zip", Roster(Acme(2_000))),
            ("Exempt Investment Advisers, March 2020", "era-0320.zip", ExemptRoster)
        );
        var sut = CreateService(client);
        while (await sut.ImportCycle(CancellationToken.None)) { }

        // A crash between the batch commit and the ledger leaves the roster to be read again.
        await using (var reset = _fixture.CreateDbContext())
            await reset
                .Set<FormAdvRosterImport>()
                .ExecuteUpdateAsync(setters =>
                    setters
                        .SetProperty(import => import.VersionedAt, (DateTime?)null)
                        .SetProperty(import => import.ParserVersion, 0)
                );
        while (await sut.ImportCycle(CancellationToken.None)) { }

        await using var verify = _fixture.CreateDbContext();
        (await verify.Set<FormAdvAdviserVersion>().CountAsync(version => version.Crd == 111))
            .Should()
            .Be(1);
    }

    private ISecEdgarClient Catalogue(params (string Label, string File, string Csv)[] links)
    {
        var client = Substitute.For<ISecEdgarClient>();
        var html = string.Concat(
            links.Select(link => $"<a href='/files/{link.File}'>{link.Label}</a>")
        );
        client
            .DownloadStream(FormAdvSnapshotCatalog.PageUrl)
            .Returns(_ => Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(html))));
        foreach (var (_, file, csv) in links.Where(link => link.Csv != null))
            client
                .DownloadStream($"https://www.sec.gov/files/{file}")
                .Returns(_ => Task.FromResult<Stream>(Zip(csv)));
        return client;
    }

    private FormAdvImportService CreateService(ISecEdgarClient client) =>
        new(
            ScopeFactory(),
            client,
            Substitute.For<ILogger<FormAdvImportService>>(),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            new FormAdvPrivateFundArchiveImporter(
                ScopeFactory(),
                client,
                Substitute.For<ILogger<FormAdvPrivateFundArchiveImporter>>()
            )
        );

    private IServiceScopeFactory ScopeFactory()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory
            .CreateScope()
            .Returns(_ =>
            {
                var context = _fixture.CreateDbContext();
                _contexts.Add(context);
                var provider = Substitute.For<IServiceProvider>();
                provider.GetService(typeof(EquiblesFinancialDbContext)).Returns(context);
                provider
                    .GetService(typeof(FormAdvRosterImportRepository))
                    .Returns(new FormAdvRosterImportRepository(context));
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(provider);
                return scope;
            });
        return scopeFactory;
    }

    private const string Header =
        "Organization CRD#,SEC#,Firm Type,Legal Name,Latest ADV Filing Date,5D(a)(1),5D(a)(3),"
        + "5D(f)(1),5D(f)(3),5F(2)(c),7B\n";

    // A new filing date alone never starts a version.
    private static string Acme(long aum, int filedMonth = 1) =>
        $"111,801-11111,Registered,ACME ADVISORS,{filedMonth:00}/15/2019,10,{aum / 2},2,{aum / 2},{aum},N\n";

    private static string Beta() => "333,801-33333,Registered,BETA LLC,01/02/2019,3,500,,,500,N\n";

    private static string Roster(params string[] rows) => Header + string.Concat(rows);

    private const string ExemptRoster =
        "Organization CRD#,SEC#,Firm Type,Legal Name,7B\n222,802-22222,ERA,VENTURE LLC,Y\n";

    private static Stream Zip(string csv)
    {
        var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var stream = archive.CreateEntry("roster.csv").Open();
            var bytes = Encoding.Latin1.GetBytes(csv);
            stream.Write(bytes, 0, bytes.Length);
        }
        buffer.Position = 0;
        return buffer;
    }
}
