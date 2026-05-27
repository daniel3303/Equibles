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
/// GH-749 guard: a NON-amendment 13F-HR whose information table parses to zero
/// holdings (wrong artifact picked, or a parse failure) must be skipped with
/// NO side effects — no phantom holding rows AND no ProcessedFiling ledger
/// entry. Recording it would permanently suppress a filing we never actually
/// ingested, so the authoritative quarterly bulk import could never backfill
/// it. Empty-is-legitimate is reserved for amendments only.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HoldingsRealtimeIngestionZeroHoldingsSkipTests : IAsyncLifetime
{
    private const string Cik = "1067983";

    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];
    private readonly CultureInfo _previousCulture;

    public HoldingsRealtimeIngestionZeroHoldingsSkipTests(ParadeDbFixture fixture)
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

    [Fact]
    public async Task IngestRecentFilings_NonAmendmentWithNoParseableHoldings_SkippedWithNoSideEffects()
    {
        var edgar = Substitute.For<ISecEdgarClient>();
        edgar
            .GetDailyIndex(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
                [
                    new EdgarDailyIndexEntry
                    {
                        FormType = "13F-HR",
                        CompanyName = "EMPTY FILER",
                        Cik = Cik,
                        DateFiled = new DateOnly(2024, 11, 20),
                        AccessionNumber = "ACC-EMPTY",
                    },
                ]
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
                var file = ci.ArgAt<string>(2);
                var xml = file.Equals("primary_doc.xml", StringComparison.OrdinalIgnoreCase)
                    ? """
                    <edgarSubmission xmlns="http://www.sec.gov/edgar/thirteenffiler">
                      <headerData><filerInfo><filer><credentials><cik>0001067983</cik></credentials></filer></filerInfo></headerData>
                      <formData><coverPage>
                        <reportCalendarOrQuarter>09-30-2024</reportCalendarOrQuarter>
                        <isAmendment>false</isAmendment>
                        <filingManager><name>EMPTY FILER</name></filingManager>
                      </coverPage></formData>
                    </edgarSubmission>
                    """
                    // Well-formed information table with NO infoTable rows.
                    : """<informationTable xmlns="http://www.sec.gov/edgar/document/thirteenf/informationtable" />""";
                return Encoding.UTF8.GetBytes(xml);
            });

        var scopeFactory = CreateScopeFactory();
        var prices = Substitute.For<IStockPriceProvider>();
        prices
            .GetClosingPrices(
                Arg.Any<IEnumerable<(Guid, DateOnly)>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(new Dictionary<(Guid, DateOnly), decimal>()));

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

        var count = await ingestion.IngestRecentFilings(
            new DateOnly(2024, 11, 25),
            1,
            new DateOnly(2024, 1, 1),
            CancellationToken.None
        );

        count.Should().Be(0);

        using var verify = FreshContext();
        (await verify.Set<InstitutionalHolding>().AnyAsync()).Should().BeFalse();
        // Not recorded: a skipped filing must remain eligible for the
        // authoritative quarterly bulk import to backfill later.
        (await verify.Set<ProcessedFiling>().AnyAsync(p => p.AccessionNumber == "ACC-EMPTY"))
            .Should()
            .BeFalse();
    }
}
