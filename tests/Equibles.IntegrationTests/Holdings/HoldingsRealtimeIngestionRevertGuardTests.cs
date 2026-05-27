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
/// The Critical guarantee of GH-749: once an original is processed and then
/// superseded by an amendment, a LATER sweep that still sees the original in
/// the daily-index look-back window must NOT re-ingest it. Without the
/// ProcessedFiling ledger, the re-swept original would upsert its stale
/// pre-amendment holdings back over the amendment (the holdings the amendment
/// deleted), silently reverting the correction.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HoldingsRealtimeIngestionRevertGuardTests : IAsyncLifetime
{
    private const string Cik = "1067983";
    private const string Cusip = "037833100";
    private static readonly DateOnly ReportDate = new(2024, 9, 30);

    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];
    private readonly CultureInfo _previousCulture;

    public HoldingsRealtimeIngestionRevertGuardTests(ParadeDbFixture fixture)
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
                sp.GetService(typeof(ProcessedFilingRepository))
                    .Returns(new ProcessedFilingRepository(ctx));
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });
        return scopeFactory;
    }

    private static string PrimaryDoc(bool isAmendment) =>
        $"""
            <edgarSubmission xmlns="http://www.sec.gov/edgar/thirteenffiler">
              <headerData><filerInfo><filer><credentials><cik>0001067983</cik></credentials></filer></filerInfo></headerData>
              <formData><coverPage>
                <reportCalendarOrQuarter>09-30-2024</reportCalendarOrQuarter>
                <isAmendment>{(isAmendment ? "true" : "false")}</isAmendment>
                <filingManager><name>BIG FUND</name></filingManager>
                <form13FFileNumber>028-1</form13FFileNumber>
              </coverPage></formData>
            </edgarSubmission>
            """;

    private static string InfoTable(long shares) =>
        $"""
            <informationTable xmlns="http://www.sec.gov/edgar/document/thirteenf/informationtable">
              <infoTable>
                <nameOfIssuer>APPLE INC</nameOfIssuer><titleOfClass>COM</titleOfClass>
                <cusip>037833100</cusip><value>1</value>
                <shrsOrPrnAmt><sshPrnamt>{shares}</sshPrnamt><sshPrnamtType>SH</sshPrnamtType></shrsOrPrnAmt>
                <investmentDiscretion>SOLE</investmentDiscretion>
                <votingAuthority><Sole>{shares}</Sole><Shared>0</Shared><None>0</None></votingAuthority>
              </infoTable>
            </informationTable>
            """;

    private static EdgarDailyIndexEntry Entry(string accession, string form) =>
        new()
        {
            FormType = form,
            CompanyName = "BIG FUND",
            Cik = Cik,
            DateFiled = new DateOnly(2024, 11, 20),
            AccessionNumber = accession,
        };

    [Fact]
    public async Task IngestRecentFilings_ReSweepingOriginalAfterAmendment_DoesNotRevertAmendment()
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
                        Cusip = Cusip,
                    }
                );
            await seed.SaveChangesAsync();
        }

        var edgar = Substitute.For<ISecEdgarClient>();
        // Sweep 1: only the original. Sweeps 2 & 3: original still inside the
        // look-back window PLUS the amendment (new accession).
        edgar
            .GetDailyIndex(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => [Entry("ACC-ORIG", "13F-HR")],
                _ => [Entry("ACC-ORIG", "13F-HR"), Entry("ACC-AMEND", "13F-HR/A")],
                _ => [Entry("ACC-ORIG", "13F-HR"), Entry("ACC-AMEND", "13F-HR/A")]
            );
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
                var accession = ci.ArgAt<string>(1);
                var file = ci.ArgAt<string>(2);
                var shares = accession == "ACC-AMEND" ? 250L : 1000L;
                var xml = file.Equals("primary_doc.xml", StringComparison.OrdinalIgnoreCase)
                    ? PrimaryDoc(isAmendment: accession == "ACC-AMEND")
                    : InfoTable(shares);
                return Encoding.UTF8.GetBytes(xml);
            });

        var scopeFactory = CreateScopeFactory();
        var prices = Substitute.For<IStockPriceProvider>();
        prices
            .GetClosingPrices(
                Arg.Any<IEnumerable<(Guid, DateOnly)>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci =>
            {
                var dict = new Dictionary<(Guid, DateOnly), decimal>();
                foreach (var (id, date) in ci.ArgAt<IEnumerable<(Guid, DateOnly)>>(0))
                    dict[(id, date)] = 100m;
                return Task.FromResult(dict);
            });

        var importService = new HoldingsImportService(
            scopeFactory,
            Substitute.For<ILogger<HoldingsImportService>>(),
            Options.Create(new WorkerOptions()),
            prices
        );
        var ingestion = new Realtime13FIngestionService(
            edgar,
            new Filing13FXmlParser(),
            new Realtime13FArchiveBuilder(),
            importService,
            scopeFactory,
            Substitute.For<ILogger<Realtime13FIngestionService>>()
        );

        var today = new DateOnly(2024, 11, 25);
        var minReportDate = new DateOnly(2024, 1, 1);

        // Sweep 1 — original. Sweep 2 — amendment supersedes it.
        await ingestion.IngestRecentFilings(today, 1, minReportDate, CancellationToken.None);
        await ingestion.IngestRecentFilings(today, 1, minReportDate, CancellationToken.None);
        // Sweep 3 — original re-listed; the ledger MUST keep it skipped.
        await ingestion.IngestRecentFilings(today, 1, minReportDate, CancellationToken.None);

        using var verify = FreshContext();
        var holdings = await verify
            .Set<InstitutionalHolding>()
            .Where(h => h.Cusip == Cusip)
            .ToListAsync();

        holdings.Should().ContainSingle();
        holdings[0].Shares.Should().Be(250);
        holdings[0].AccessionNumber.Should().Be("ACC-AMEND");
        holdings[0].IsAmendment.Should().BeTrue();
    }
}
