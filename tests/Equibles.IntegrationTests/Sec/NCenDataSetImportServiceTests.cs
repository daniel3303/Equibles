using System.IO.Compression;
using System.Text;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Sec.Contracts;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// The N-CEN data set import against Postgres, over real rows sliced from SEC's 2018 Q3 and 2026 Q3
/// archives: quarters import oldest first under a per-cycle cap, the ledger skips imported
/// quarters, a re-posted or re-parsed quarter replaces its rows, and an amendment supersedes the
/// funds it restates, across quarters too, while a series it omits stays current.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class NCenDataSetImportServiceTests : IAsyncLifetime
{
    private const string Folder = "https://www.sec.gov/files/dera/data/form-n-cen-data-sets/";
    private const string MasonOriginal = "0001193125-26-386137";
    private const string MasonAmendment = "0001193125-26-388800";

    private static readonly string[] Tables =
    [
        "SUBMISSION",
        "REGISTRANT",
        "FUND_REPORTED_INFO",
        "ADVISER",
        "SHARES_OUTSTANDING",
        "SECURITY_EXCHANGE",
    ];

    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];
    private readonly ISecEdgarClient _edgar = Substitute.For<ISecEdgarClient>();
    private readonly List<string> _catalog = [];
    private readonly IServiceScopeFactory _reporterScopes = Substitute.For<IServiceScopeFactory>();

    public NCenDataSetImportServiceTests(ParadeDbFixture fixture)
    {
        _fixture = fixture;
        _edgar
            .DownloadStream(NCenDataSetCatalog.PageUrl)
            .Returns(_ =>
                Task.FromResult<Stream>(
                    new MemoryStream(
                        Encoding.UTF8.GetBytes(
                            string.Concat(
                                _catalog.Select(file => $"<a href=\"{Folder}{file}\">{file}</a>")
                            )
                        )
                    )
                )
            );
    }

    public async Task InitializeAsync() => await _fixture.ResetAsync();

    public Task DisposeAsync()
    {
        foreach (var context in _contexts)
            context.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task ImportPending_ImportsEveryQuarter_AndMarksRestatedFundsSuperseded()
    {
        List("2018q3_ncen.zip", Archive("2018q3"));
        List("2026q3_ncen.zip", Archive("2026q3"));

        (await Sut().ImportPending(4, CancellationToken.None)).Should().BeFalse();

        await using var verify = _fixture.CreateDbContext();
        var ledger = await verify.Set<NCenDataSetImport>().OrderBy(row => row.Year).ToListAsync();
        ledger
            .Select(row => (row.Year, row.Quarter, row.ReportCount, row.ParserVersion))
            .Should()
            .Equal((2018, 3, 3, 1), (2026, 3, 6, 1));
        var ishares = await verify
            .Set<NCenRegistrantReport>()
            .SingleAsync(report => report.AccessionNumber == "0001004726-26-005736");
        ishares.RegistrantCik.Should().Be("930667");
        ishares.IsFamilyInvestmentCompany.Should().BeTrue();
        var etf = await verify
            .Set<NCenFundReport>()
            .Include(fund => fund.Advisers)
            .Include(fund => fund.ExchangeListings)
            .SingleAsync(fund => fund.SeriesId == "S000035998");
        etf.FundTypes.Should().Be(NCenFundTypes.ExchangeTradedFund | NCenFundTypes.IndexFund);
        etf.RegistrantCik.Should().Be("930667");
        etf.Advisers.Single().Crd.Should().Be(105247);
        etf.ExchangeListings.Single().Ticker.Should().Be("DVYA");

        (await Superseded(verify, MasonOriginal)).Should().BeTrue();
        (await Superseded(verify, MasonAmendment)).Should().BeFalse();
        (await Superseded(verify, "0000869392-18-000195")).Should().BeTrue();
        (
            await verify
                .Set<NCenFundReport>()
                .CountAsync(fund =>
                    fund.AccessionNumber == "0000910472-26-011642" && fund.IsSuperseded
                )
        )
            .Should()
            .Be(0);
    }

    [Fact]
    public async Task ImportPending_CapReached_ImportsTheOldestQuarterFirst()
    {
        List("2026q3_ncen.zip", Archive("2026q3"));
        List("2018q3_ncen.zip", Archive("2018q3"));

        (await Sut().ImportPending(1, CancellationToken.None)).Should().BeTrue();

        await using var verify = _fixture.CreateDbContext();
        (await verify.Set<NCenDataSetImport>().SingleAsync()).Year.Should().Be(2018);
        await _edgar.DidNotReceive().DownloadStream(Folder + "2026q3_ncen.zip");
    }

    [Fact]
    public async Task ImportedQuarter_IsNotDownloadedAgain()
    {
        List("2026q3_ncen.zip", Archive("2026q3"));

        await Sut().ImportPending(4, CancellationToken.None);
        await Sut().ImportPending(4, CancellationToken.None);

        await _edgar.Received(1).DownloadStream(Folder + "2026q3_ncen.zip");
    }

    [Fact]
    public async Task RepostedQuarter_ReplacesItsReports_AndRemarksTheirRegistrants()
    {
        List("2026q3_ncen.zip", Archive("2026q3"));
        await Sut().ImportPending(4, CancellationToken.None);

        // SEC re-posts the quarter without the amendment: the original is current again.
        _catalog.Clear();
        List("2026q3_ncen_0.zip", Archive("2026q3", without: MasonAmendment));
        await Sut().ImportPending(4, CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();
        (await verify.Set<NCenDataSetImport>().SingleAsync())
            .FileName.Should()
            .Be("2026q3_ncen_0.zip");
        (await verify.Set<NCenRegistrantReport>().CountAsync()).Should().Be(5);
        (
            await verify
                .Set<NCenFundReport>()
                .CountAsync(fund => fund.AccessionNumber == MasonAmendment)
        )
            .Should()
            .Be(0);
        (await Superseded(verify, MasonOriginal)).Should().BeFalse();
    }

    [Fact]
    public async Task OlderParserVersion_ReimportsTheQuarter()
    {
        List("2026q3_ncen.zip", Archive("2026q3"));
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Add(
                new NCenDataSetImport
                {
                    Year = 2026,
                    Quarter = 3,
                    FileName = "2026q3_ncen.zip",
                    ParserVersion = NCenDataSetImportService.ParserVersion - 1,
                }
            );
            await seed.SaveChangesAsync();
        }

        await Sut().ImportPending(4, CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();
        (await verify.Set<NCenDataSetImport>().SingleAsync())
            .ParserVersion.Should()
            .Be(NCenDataSetImportService.ParserVersion);
        (await verify.Set<NCenRegistrantReport>().CountAsync()).Should().Be(6);
    }

    [Fact]
    public async Task AmendmentInALaterQuarter_SupersedesTheEarlierReport()
    {
        List("2026q2_ncen.zip", Archive("2026q3", without: MasonAmendment));
        List("2026q3_ncen.zip", Archive("2026q3", only: MasonAmendment));

        await Sut().ImportPending(4, CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();
        (await Superseded(verify, MasonOriginal)).Should().BeTrue();
        (await Superseded(verify, MasonAmendment)).Should().BeFalse();
    }

    [Fact]
    public async Task AmendmentOmittingASeries_LeavesThatSeriesCurrent()
    {
        // Keep the amendment's submission and registrant rows but drop its fund rows.
        List("2026q3_ncen.zip", Archive("2026q3", withoutFundsOf: MasonAmendment));

        await Sut().ImportPending(4, CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();
        (
            await verify
                .Set<NCenRegistrantReport>()
                .AnyAsync(r => r.AccessionNumber == MasonAmendment)
        )
            .Should()
            .BeTrue();
        (await Superseded(verify, MasonOriginal)).Should().BeFalse();
    }

    [Fact]
    public async Task UnreadableQuarter_WaitsADay_WithoutBlockingLaterQuarters()
    {
        List("2018q3_ncen.zip", Archive("2018q3", omit: "ADVISER"));
        List("2026q3_ncen.zip", Archive("2026q3"));

        (await Sut().ImportPending(1, CancellationToken.None)).Should().BeTrue();
        (await Sut().ImportPending(1, CancellationToken.None)).Should().BeFalse();

        await using var verify = _fixture.CreateDbContext();
        var ledger = await verify.Set<NCenDataSetImport>().OrderBy(row => row.Year).ToListAsync();
        ledger[0].FileName.Should().BeNull();
        ledger[0].LastFailure.Should().Contain("ADVISER");
        ledger[0].LastFailureAt.Should().NotBeNull();
        ledger[1].FileName.Should().Be("2026q3_ncen.zip");
        await _edgar.Received(1).DownloadStream(Folder + "2018q3_ncen.zip");
        _reporterScopes.ReceivedWithAnyArgs().CreateScope();
    }

    [Fact]
    public async Task FailedQuarter_IsRetriedAfterTheDelay()
    {
        List("2026q3_ncen.zip", Archive("2026q3"));
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Add(
                new NCenDataSetImport
                {
                    Year = 2026,
                    Quarter = 3,
                    LastFailureAt =
                        DateTime.UtcNow
                        - NCenDataSetImportService.FailureRetryDelay
                        - TimeSpan.FromMinutes(1),
                    LastFailure = "earlier failure",
                }
            );
            await seed.SaveChangesAsync();
        }

        await Sut().ImportPending(4, CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();
        var row = await verify.Set<NCenDataSetImport>().SingleAsync();
        row.FileName.Should().Be("2026q3_ncen.zip");
        row.LastFailure.Should().BeNull();
    }

    [Fact]
    public async Task SameDayAmendment_IsCurrentByAccessionOrder()
    {
        List(
            "2026q3_ncen.zip",
            Archive(
                "2026q3",
                rewrite: (table, line) =>
                    [
                        table == "SUBMISSION" && line.StartsWith(MasonAmendment)
                            ? line.Replace("11-SEP-2026", "09-SEP-2026")
                            : line,
                    ]
            )
        );

        await Sut().ImportPending(4, CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();
        (await Superseded(verify, MasonOriginal)).Should().BeTrue();
        (await Superseded(verify, MasonAmendment)).Should().BeFalse();
    }

    [Fact]
    public async Task AmendmentOfARegistrantWithoutSeries_SupersedesItsFund()
    {
        const string original = "0000940400-26-026182";
        const string amendment = "0000940400-26-026999";
        List(
            "2026q3_ncen.zip",
            Archive(
                "2026q3",
                rewrite: (table, line) =>
                    !line.Contains(original)
                        ? [line]
                        :
                        [
                            line,
                            table == "SUBMISSION"
                                ? line.Replace(original + "\tN-CEN\t", amendment + "\tN-CEN/A\t")
                                    .Replace("07-JUL-2026", "08-JUL-2026")
                                : line.Replace(original, amendment),
                        ]
            )
        );

        await Sut().ImportPending(4, CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();
        (await Superseded(verify, original)).Should().BeTrue();
        (await Superseded(verify, amendment)).Should().BeFalse();
    }

    [Fact]
    public async Task RepostDroppingARegistrant_RemarksItsReportsInOtherQuarters()
    {
        List("2026q2_ncen.zip", Archive("2026q3", without: MasonAmendment));
        List("2026q3_ncen.zip", Archive("2026q3", only: MasonAmendment));
        await Sut().ImportPending(4, CancellationToken.None);

        // SEC re-posts Q3 without Mason at all; the Q2 original is current again.
        _catalog.Remove("2026q3_ncen.zip");
        List("2026q3_ncen_0.zip", Archive("2026q3", only: "0000856517-26-000004"));
        await Sut().ImportPending(4, CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();
        (await Superseded(verify, MasonOriginal)).Should().BeFalse();
    }

    [Fact]
    public async Task AccessionPublishedInTwoQuarters_FailsTheLaterQuarterLoudly()
    {
        List("2026q2_ncen.zip", Archive("2026q3", only: MasonOriginal));
        List("2026q3_ncen.zip", Archive("2026q3"));

        await Sut().ImportPending(4, CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();
        (
            await verify
                .Set<NCenRegistrantReport>()
                .SingleAsync(r => r.AccessionNumber == MasonOriginal)
        )
            .DataSetQuarter.Should()
            .Be(2);
        var later = await verify.Set<NCenDataSetImport>().SingleAsync(row => row.Quarter == 3);
        later.FileName.Should().BeNull();
        later.LastFailure.Should().Contain(MasonOriginal).And.Contain("2026 Q2");
        (await verify.Set<NCenRegistrantReport>().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ArchiveSecNoLongerServes_IsRecordedAndLaterQuartersStillImport()
    {
        _catalog.Add("2018q3_ncen.zip");
        _edgar
            .DownloadStream(Folder + "2018q3_ncen.zip")
            .Returns<Task<Stream>>(_ =>
                throw new HttpRequestException(
                    "Not Found",
                    null,
                    System.Net.HttpStatusCode.NotFound
                )
            );
        List("2026q3_ncen.zip", Archive("2026q3"));

        (await Sut().ImportPending(1, CancellationToken.None)).Should().BeTrue();
        (await Sut().ImportPending(1, CancellationToken.None)).Should().BeFalse();

        await using var verify = _fixture.CreateDbContext();
        var ledger = await verify.Set<NCenDataSetImport>().OrderBy(row => row.Year).ToListAsync();
        ledger[0].LastFailureAt.Should().NotBeNull();
        ledger[1].FileName.Should().Be("2026q3_ncen.zip");
    }

    private static Task<bool> Superseded(EquiblesFinancialDbContext context, string accession) =>
        context
            .Set<NCenFundReport>()
            .Where(fund => fund.AccessionNumber == accession)
            .Select(fund => fund.IsSuperseded)
            .SingleAsync();

    private void List(string file, byte[] archive)
    {
        _catalog.Add(file);
        _edgar
            .DownloadStream(Folder + file)
            .Returns(_ => Task.FromResult<Stream>(new MemoryStream(archive)));
    }

    private NCenDataSetImportService Sut()
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
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(provider);
                return scope;
            });
        return new NCenDataSetImportService(
            scopeFactory,
            _edgar,
            new ErrorReporter(_reporterScopes, Substitute.For<ILogger<ErrorReporter>>()),
            Substitute.For<ILogger<NCenDataSetImportService>>()
        );
    }

    // Builds an archive from the fixture slice: drop or keep one accession's rows, drop only its fund
    // rows, omit a table, or rewrite each data line into zero or more lines.
    private static byte[] Archive(
        string vintage,
        string without = null,
        string only = null,
        string withoutFundsOf = null,
        string omit = null,
        Func<string, string, IEnumerable<string>> rewrite = null
    )
    {
        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var table in Tables.Where(table => table != omit))
            {
                var lines = File.ReadAllLines(
                    Path.Combine(
                        AppContext.BaseDirectory,
                        "TestAssets",
                        "NCen",
                        vintage,
                        table + ".tsv"
                    )
                );
                var rows = lines
                    .Skip(1)
                    .Where(line => without == null || !line.Contains(without))
                    .Where(line => only == null || line.Contains(only))
                    .Where(line => withoutFundsOf == null || !line.Contains(withoutFundsOf + "_"))
                    .SelectMany(line => rewrite == null ? [line] : rewrite(table, line));
                var entry = archive.CreateEntry(table + ".tsv");
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(string.Join("\n", rows.Prepend(lines[0])) + "\n");
            }
        }
        return zip.ToArray();
    }
}
