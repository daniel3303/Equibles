using System.Globalization;
using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Core.Contracts;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.Holdings.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// 13F twin of the 13D/G incomplete-import pin (EquiblesCommercial#2850): the
/// import service returns IsComplete=false for the NoTrackedStocks outcome —
/// its own contract says "leave the data set unprocessed so a later cycle
/// backfills it once CUSIPs exist" — but the 13F realtime sweep recorded the
/// accession in ProcessedFiling anyway, consuming the filing forever. Pin: an
/// incomplete import stays unrecorded and uncounted, keeping the filing
/// retryable, and does not hold the watermark back (the quarterly bulk import
/// reconciles it regardless).
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class Realtime13FIngestionIncompleteImportTests : IAsyncLifetime
{
    private const string Accession = "0004000000-26-000001";
    private const string UntrackedCusip = "999999999";

    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];
    private readonly CultureInfo _previousCulture;

    public Realtime13FIngestionIncompleteImportTests(ParadeDbFixture fixture)
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
                sp.GetService(typeof(ProcessedFilingRepository))
                    .Returns(new ProcessedFilingRepository(ctx));
                sp.GetService(typeof(HoldingsImportFailureRepository))
                    .Returns(new HoldingsImportFailureRepository(ctx));
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });
        return scopeFactory;
    }

    private static string PrimaryDoc() =>
        """
            <edgarSubmission xmlns="http://www.sec.gov/edgar/thirteenffiler">
              <headerData><filerInfo><filer><credentials><cik>0001067984</cik></credentials></filer></filerInfo></headerData>
              <formData><coverPage>
                <reportCalendarOrQuarter>09-30-2024</reportCalendarOrQuarter>
                <isAmendment>false</isAmendment>
                <filingManager><name>UNSEEDED FUND</name></filingManager>
                <form13FFileNumber>028-2</form13FFileNumber>
              </coverPage></formData>
            </edgarSubmission>
            """;

    private static string InfoTable() =>
        $"""
            <informationTable xmlns="http://www.sec.gov/edgar/document/thirteenf/informationtable">
              <infoTable>
                <nameOfIssuer>UNSEEDED ISSUER INC</nameOfIssuer><titleOfClass>COM</titleOfClass>
                <cusip>{UntrackedCusip}</cusip><value>1</value>
                <shrsOrPrnAmt><sshPrnamt>1000</sshPrnamt><sshPrnamtType>SH</sshPrnamtType></shrsOrPrnAmt>
                <investmentDiscretion>SOLE</investmentDiscretion>
                <votingAuthority><Sole>1000</Sole><Shared>0</Shared><None>0</None></votingAuthority>
              </infoTable>
            </informationTable>
            """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfirmedZeroOriginal_ResolvesCompleteRecoveryOnlyForAnEmptyQuarter(
        bool retainedBook
    )
    {
        const string cik = "1634047";
        const string accession = "0001172661-23-003928";
        var filed = new DateOnly(2023, 11, 14);
        var report = new DateOnly(2023, 9, 30);
        using var db = FreshContext();
        if (retainedBook)
        {
            db.Add(
                new InstitutionalHolding
                {
                    Issuer = new EquityIssuer { Name = "Existing issuer" },
                    InstitutionalHolder = new InstitutionalHolder
                    {
                        Cik = cik,
                        Name = "Existing filer",
                    },
                    FilingDate = filed.AddDays(-1),
                    ReportDate = report,
                    FilingType = FilingType.Form13F,
                    AccessionNumber = "retained-earlier-filing",
                    Cusip = "111111111",
                    Shares = 10,
                    Value = 100,
                }
            );
            await db.SaveChangesAsync();
        }
        var failures = new HoldingsImportFailureRepository(db);
        await failures.Record(
            accession,
            cik,
            filed,
            report,
            HoldingsImportFailureReason.Incomplete,
            CancellationToken.None
        );
        var edgar = Substitute.For<ISecEdgarClient>();
        edgar
            .GetDocumentContent(accession, cik, Arg.Any<CancellationToken>())
            .Returns(
                File.ReadAllText(
                    Path.Combine(
                        AppContext.BaseDirectory,
                        "TestAssets",
                        "Holdings",
                        "13f-zero-position-submission.txt"
                    )
                )
            );
        edgar
            .GetCompanyFilings(cik, null, filed, Arg.Any<DateOnly?>())
            .Returns([
                new FilingData
                {
                    AccessionNumber = accession,
                    Form = "13F-HR",
                    FilingDate = filed,
                    ReportDate = report,
                },
            ]);
        var scopes = CreateScopeFactory();
        var importer = new HoldingsImportService(
            scopes,
            Substitute.For<ILogger<HoldingsImportService>>(),
            Options.Create(new WorkerOptions()),
            Substitute.For<IStockPriceProvider>(),
            Substitute.For<MassTransit.IBus>()
        );
        var ingestion = new Realtime13FIngestionService(
            edgar,
            new Filing13FXmlParser(),
            new Realtime13FArchiveBuilder(),
            importer,
            scopes,
            Substitute.For<ILogger<Realtime13FIngestionService>>()
        );
        var recovery = new HoldingsImportRecoveryService(
            failures,
            new InstitutionalHoldingRepository(db),
            edgar,
            ingestion,
            Substitute.For<ILogger<HoldingsImportRecoveryService>>()
        );

        await recovery.Recover(new DateOnly(2020, 1, 1), CancellationToken.None);

        using var verify = FreshContext();
        (await verify.Set<ProcessedFiling>().AnyAsync(p => p.AccessionNumber == accession))
            .Should()
            .Be(!retainedBook);
        var failure = await verify.Set<HoldingsImportFailure>().SingleAsync();
        failure.ResolvedAt.HasValue.Should().Be(!retainedBook);
        var holdings = await verify.Set<InstitutionalHolding>().ToListAsync();
        holdings.Should().HaveCount(retainedBook ? 1 : 0);
        if (retainedBook)
        {
            holdings[0].Shares.Should().Be(10);
            holdings[0].Value.Should().Be(100);
            holdings[0].AccessionNumber.Should().Be("retained-earlier-filing");
        }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    public async Task IngestRecentFilings_ImportReportsIncomplete_LeavesAccessionUnrecordedForRetry(
        bool completeSubmission,
        bool textUnavailable,
        bool inconsistentDeclarations
    )
    {
        // No CommonStock is seeded for the filing's CUSIP, so the import maps no
        // tracked stock and returns IsComplete=false ("retry once CUSIPs exist").
        var entry = new EdgarDailyIndexEntry
        {
            FormType = "13F-HR",
            CompanyName = "UNSEEDED FUND",
            Cik = "1067984",
            DateFiled = new DateOnly(2024, 11, 20),
            AccessionNumber = Accession,
        };

        var edgar = Substitute.For<ISecEdgarClient>();
        if (textUnavailable)
            edgar
                .GetDocumentContent(Accession, entry.Cik, Arg.Any<CancellationToken>())
                .Returns<string>(_ => throw new HttpRequestException("Submission unavailable"));
        if (completeSubmission)
        {
            var cover = PrimaryDoc()
                .Replace(
                    "</formData>",
                    "<summaryPage><tableEntryTotal>1</tableEntryTotal><tableValueTotal>1</tableValueTotal></summaryPage></formData>"
                );
            if (inconsistentDeclarations)
                cover = cover.Replace("<tableEntryTotal>1", "<tableEntryTotal>2");
            var submission = $"""
                <SEC-DOCUMENT>
                <DOCUMENT>
                <TYPE>13F-HR
                <FILENAME>primary_doc.xml
                <TEXT><XML>{cover}</XML></TEXT>
                </DOCUMENT>
                <DOCUMENT>
                <TYPE>INFORMATION TABLE
                <FILENAME>infotable.xml
                <TEXT><XML>{InfoTable()}</XML></TEXT>
                </DOCUMENT>
                </SEC-DOCUMENT>
                """;
            edgar
                .GetDocumentContent(Accession, entry.Cik, Arg.Any<CancellationToken>())
                .Returns(submission);
        }
        edgar
            .GetDailyIndex(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(_ => [entry]);
        edgar
            .GetFilingArtifactNames(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(["primary_doc.xml", "infotable.xml"]);
        edgar
            .GetDocumentFileBytes(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci =>
            {
                var file = ci.ArgAt<string>(2);
                var xml = file.Equals("primary_doc.xml", StringComparison.OrdinalIgnoreCase)
                    ? PrimaryDoc()
                    : InfoTable();
                return Encoding.UTF8.GetBytes(xml);
            });

        var scopeFactory = CreateScopeFactory();

        var prices = Substitute.For<IStockPriceProvider>();
        prices
            .GetClosingPrices(
                Arg.Any<IEnumerable<(Guid, string, DateOnly)>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(new Dictionary<(Guid, string, DateOnly), decimal>()));

        var importService = new HoldingsImportService(
            scopeFactory,
            Substitute.For<ILogger<HoldingsImportService>>(),
            Options.Create(new WorkerOptions()),
            prices,
            Substitute.For<MassTransit.IBus>()
        );
        var ingestion = new Realtime13FIngestionService(
            edgar,
            new Filing13FXmlParser(),
            new Realtime13FArchiveBuilder(),
            importService,
            scopeFactory,
            Substitute.For<ILogger<Realtime13FIngestionService>>()
        );

        var result = await ingestion.IngestRecentFilings(
            today: new DateOnly(2024, 11, 25),
            lookbackDays: 1,
            minReportDate: new DateOnly(2024, 1, 1),
            CancellationToken.None
        );

        result.FilingsImported.Should().Be(0, "an incomplete import is not an import");
        result
            .EarliestFailedDate.Should()
            .BeNull(
                "an incomplete import must not hold the watermark back — the quarterly bulk import reconciles it"
            );

        using var verify = FreshContext();
        var processed = await verify
            .Set<ProcessedFiling>()
            .Select(p => p.AccessionNumber)
            .ToListAsync();
        processed
            .Should()
            .NotContain(
                Accession,
                "an incomplete import must stay unrecorded so a later cycle retries it"
            );
        if (completeSubmission)
            await edgar
                .DidNotReceive()
                .GetFilingArtifactNames(
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>()
                );
        if (inconsistentDeclarations)
        {
            await edgar
                .Received(1)
                .GetDocumentFileBytes(
                    entry.Cik,
                    Accession,
                    "primary_doc.xml",
                    Arg.Any<CancellationToken>()
                );
            await edgar
                .Received(1)
                .GetDocumentFileBytes(
                    entry.Cik,
                    Accession,
                    "infotable.xml",
                    Arg.Any<CancellationToken>()
                );
        }
    }
}
