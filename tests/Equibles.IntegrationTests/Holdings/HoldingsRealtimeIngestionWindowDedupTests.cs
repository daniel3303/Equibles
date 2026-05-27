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
/// DiscoverEntries documents: "Deduplicate by accession across the window …
/// this only collapses the same filing re-listed across overlapping sweeps".
/// Every existing test runs with lookbackDays = 1 so the multi-day window
/// dedup is unexercised. With a multi-day look-back where EDGAR re-lists the
/// same accession on each day, the filing must be parsed and imported exactly
/// once — not once per day in the window.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HoldingsRealtimeIngestionWindowDedupTests : IAsyncLifetime
{
    private const string Cik = "1067983";
    private const string Cusip = "037833100";

    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];
    private readonly CultureInfo _previousCulture;

    public HoldingsRealtimeIngestionWindowDedupTests(ParadeDbFixture fixture)
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

    private static string PrimaryDoc() =>
        """
            <edgarSubmission xmlns="http://www.sec.gov/edgar/thirteenffiler">
              <headerData><filerInfo><filer><credentials><cik>0001067983</cik></credentials></filer></filerInfo></headerData>
              <formData><coverPage>
                <reportCalendarOrQuarter>09-30-2024</reportCalendarOrQuarter>
                <isAmendment>false</isAmendment>
                <filingManager><name>BIG FUND</name></filingManager>
                <form13FFileNumber>028-1</form13FFileNumber>
              </coverPage></formData>
            </edgarSubmission>
            """;

    private static string InfoTable() =>
        """
            <informationTable xmlns="http://www.sec.gov/edgar/document/thirteenf/informationtable">
              <infoTable>
                <nameOfIssuer>APPLE INC</nameOfIssuer><titleOfClass>COM</titleOfClass>
                <cusip>037833100</cusip><value>1</value>
                <shrsOrPrnAmt><sshPrnamt>1000</sshPrnamt><sshPrnamtType>SH</sshPrnamtType></shrsOrPrnAmt>
                <investmentDiscretion>SOLE</investmentDiscretion>
                <votingAuthority><Sole>1000</Sole><Shared>0</Shared><None>0</None></votingAuthority>
              </infoTable>
            </informationTable>
            """;

    private static EdgarDailyIndexEntry Entry() =>
        new()
        {
            FormType = "13F-HR",
            CompanyName = "BIG FUND",
            Cik = Cik,
            DateFiled = new DateOnly(2024, 11, 20),
            AccessionNumber = "ACC-DUP",
        };

    [Fact]
    public async Task IngestRecentFilings_SameAccessionReListedAcrossWindowDays_ImportedExactlyOnce()
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
        // EDGAR re-lists the same accession on every day of the look-back window.
        edgar
            .GetDailyIndex(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(_ => [Entry()]);
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

        // 3-day look-back: the same accession is seen on all 3 days.
        var imported = await ingestion.IngestRecentFilings(
            today,
            3,
            minReportDate,
            CancellationToken.None
        );

        imported.Should().Be(1, "the re-listed accession must be collapsed to one filing");

        // Parsed once, not once per window day.
        await edgar
            .Received(1)
            .GetFilingArtifactNames("1067983", "ACC-DUP", Arg.Any<CancellationToken>());

        using var verify = FreshContext();
        var holdings = await verify
            .Set<InstitutionalHolding>()
            .Where(h => h.Cusip == Cusip)
            .ToListAsync();

        holdings.Should().ContainSingle("a re-listed filing must not produce duplicate holdings");
        holdings[0].AccessionNumber.Should().Be("ACC-DUP");
    }
}
