using System.Globalization;
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
using Equibles.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// A 13F whose source positions all resolve to no tracked security (bonds, warrants, mandatory
/// units, delisted or mistyped CUSIPs, or the all-zero placeholder row filers use for an empty
/// quarter) has nothing to store once the identity directory is seeded. Pin: recovery completes
/// it instead of replaying the filer's tail every cycle, except where completing would hide a
/// retained book that an original or restatement says nothing about.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class Realtime13FIngestionUntrackedOnlyTests : IAsyncLifetime
{
    private const string Cik = "1067984";
    private const string Accession = "0004000000-26-000002";
    private static readonly DateOnly Filed = new(2024, 11, 20);
    private static readonly DateOnly Report = new(2024, 9, 30);

    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];
    private readonly CultureInfo _previousCulture;

    public Realtime13FIngestionUntrackedOnlyTests(ParadeDbFixture fixture)
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

    private static string Cover(string formType, string amendmentType, int entries, long value)
    {
        var amendment =
            amendmentType == null
                ? "<isAmendment>false</isAmendment>"
                : $"<isAmendment>true</isAmendment><amendmentInfo><amendmentType>{amendmentType}</amendmentType></amendmentInfo>";
        return $"""
            <edgarSubmission xmlns="http://www.sec.gov/edgar/thirteenffiler">
              <headerData>
                <submissionType>{formType}</submissionType>
                <filerInfo>
                  <filer><credentials><cik>0001067984</cik></credentials></filer>
                  <periodOfReport>09-30-2024</periodOfReport>
                </filerInfo>
              </headerData>
              <formData>
                <coverPage>
                  <reportCalendarOrQuarter>09-30-2024</reportCalendarOrQuarter>
                  {amendment}
                  <filingManager><name>UNTRACKED FUND</name></filingManager>
                  <reportType>13F HOLDINGS REPORT</reportType>
                  <form13FFileNumber>028-2</form13FFileNumber>
                </coverPage>
                <summaryPage>
                  <otherIncludedManagersCount>0</otherIncludedManagersCount>
                  <tableEntryTotal>{entries}</tableEntryTotal>
                  <tableValueTotal>{value}</tableValueTotal>
                </summaryPage>
              </formData>
            </edgarSubmission>
            """;
    }

    private static string Table(string cusip, long value, long shares) =>
        $"""
            <informationTable xmlns="http://www.sec.gov/edgar/document/thirteenf/informationtable">
              <infoTable>
                <nameOfIssuer>SOME ISSUER</nameOfIssuer><titleOfClass>COM</titleOfClass>
                <cusip>{cusip}</cusip><value>{value}</value>
                <shrsOrPrnAmt><sshPrnamt>{shares}</sshPrnamt><sshPrnamtType>SH</sshPrnamtType></shrsOrPrnAmt>
                <investmentDiscretion>SOLE</investmentDiscretion>
                <votingAuthority><Sole>{shares}</Sole><Shared>0</Shared><None>0</None></votingAuthority>
              </infoTable>
            </informationTable>
            """;

    private static string Submission(string formType, string cover, string table) =>
        $"""
            <SEC-DOCUMENT>
            <DOCUMENT>
            <TYPE>{formType}
            <FILENAME>primary_doc.xml
            <TEXT><XML>{cover}</XML></TEXT>
            </DOCUMENT>
            <DOCUMENT>
            <TYPE>INFORMATION TABLE
            <FILENAME>infotable.xml
            <TEXT><XML>{table}</XML></TEXT>
            </DOCUMENT>
            </SEC-DOCUMENT>
            """;

    // shape: original | new-holdings | restatement | placeholder (declared 0 entries, one all-zero
    // 000000000 row, the form filers use for an empty quarter; its totals do not reconcile, so it
    // travels the artifact route rather than the verified-submission route).
    // retained: none | earlier (an earlier filing's rows) | later (a later filing's rows) |
    // same-day-later (filed the same day under a higher accession) | own (this filing's rows).
    [Theory]
    [InlineData("original", "none", true)]
    [InlineData("original", "earlier", false)]
    [InlineData("original", "later", true)]
    [InlineData("original", "same-day-later", true)]
    [InlineData("original", "own", false)]
    [InlineData("new-holdings", "earlier", true)]
    [InlineData("restatement", "earlier", false)]
    [InlineData("restatement", "later", true)]
    [InlineData("placeholder", "none", true)]
    public async Task UntrackedOnlyFiling_CompletesRecoveryUnlessItWouldHideARetainedBook(
        string shape,
        string retained,
        bool expectComplete
    )
    {
        var retainedBook = retained != "none";
        var laterRetained = retained is "later" or "same-day-later";
        var (retainedFiled, retainedAccession) = retained switch
        {
            "earlier" => (Filed.AddDays(-1), "0004000000-26-000001"),
            "later" => (Filed.AddDays(1), "0004000000-26-000003"),
            "same-day-later" => (Filed, "0004000000-26-000003"),
            "own" => (Filed, Accession),
            _ => (default, null),
        };
        var formType = shape is "new-holdings" or "restatement" ? "13F-HR/A" : "13F-HR";
        var amendmentType = shape switch
        {
            "new-holdings" => "NEW HOLDINGS",
            "restatement" => "RESTATEMENT",
            _ => null,
        };
        var cover =
            shape == "placeholder"
                ? Cover(formType, null, 0, 0)
                : Cover(formType, amendmentType, 1, 1);
        var table = shape == "placeholder" ? Table("000000000", 0, 0) : Table("999999999", 1, 1000);

        using var db = FreshContext();
        var tracked = EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "AAPL",
            Name: "Apple",
            Cusip: "037833100"
        );
        db.Add(tracked);
        if (retainedBook)
            db.Add(
                new InstitutionalHolding
                {
                    Issuer = tracked,
                    InstitutionalHolder = new InstitutionalHolder
                    {
                        Cik = Cik,
                        Name = "UNTRACKED FUND",
                    },
                    FilingDate = retainedFiled,
                    ReportDate = Report,
                    FilingType = FilingType.Form13F,
                    AccessionNumber = retainedAccession,
                    Cusip = "037833100",
                    Shares = 10,
                    Value = 100,
                }
            );
        // A later filing's rows are retained only while the submissions index offers that filing;
        // its processed marker lets the walk skip it, since the pending filing writes nothing.
        if (laterRetained)
            db.Add(new ProcessedFiling { AccessionNumber = retainedAccession });
        await db.SaveChangesAsync();
        var failures = new HoldingsImportFailureRepository(db);
        await failures.Record(
            Accession,
            Cik,
            Filed,
            Report,
            HoldingsImportFailureReason.Incomplete,
            CancellationToken.None
        );

        var edgar = Substitute.For<ISecEdgarClient>();
        edgar
            .GetDocumentContent(Accession, Cik, Arg.Any<CancellationToken>())
            .Returns(Submission(formType, cover, table));
        edgar
            .GetDocumentFileBytes(Cik, Accession, "primary_doc.xml", Arg.Any<CancellationToken>())
            .Returns(System.Text.Encoding.UTF8.GetBytes(cover));
        edgar
            .GetDocumentFileBytes(Cik, Accession, "infotable.xml", Arg.Any<CancellationToken>())
            .Returns(System.Text.Encoding.UTF8.GetBytes(table));
        var offered = new List<FilingData>
        {
            new()
            {
                AccessionNumber = Accession,
                Form = formType,
                FilingDate = Filed,
                ReportDate = Report,
            },
        };
        if (laterRetained)
            offered.Add(
                new()
                {
                    AccessionNumber = retainedAccession,
                    Form = "13F-HR/A",
                    FilingDate = retainedFiled,
                    ReportDate = Report,
                }
            );
        edgar.GetCompanyFilings(Cik, null, Filed, Arg.Any<DateOnly?>()).Returns(offered);
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
        (await verify.Set<ProcessedFiling>().AnyAsync(p => p.AccessionNumber == Accession))
            .Should()
            .Be(expectComplete);
        var failure = await verify.Set<HoldingsImportFailure>().SingleAsync();
        failure.ResolvedAt.HasValue.Should().Be(expectComplete);
        var holdings = await verify.Set<InstitutionalHolding>().ToListAsync();
        holdings.Should().HaveCount(retainedBook ? 1 : 0, "nothing tracked is ever written");
        if (retainedBook)
        {
            holdings[0].AccessionNumber.Should().Be(retainedAccession);
            holdings[0].FilingDate.Should().Be(retainedFiled);
            holdings[0].Shares.Should().Be(10);
        }
        if (laterRetained)
            (await verify.Set<ProcessedFiling>().CountAsync())
                .Should()
                .Be(expectComplete ? 2 : 1, "the later filing's marker is kept, never re-imported");
    }
}
