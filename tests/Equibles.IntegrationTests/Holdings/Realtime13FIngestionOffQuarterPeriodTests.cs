using System.Globalization;
using System.Text;
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
/// A Form 13F whose period of report is not a calendar quarter end is refused by the realtime
/// sweep before any holdings write: it lands in the recovery queue under its own reason, with
/// the offending date, and is neither stored nor marked processed.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class Realtime13FIngestionOffQuarterPeriodTests : IAsyncLifetime
{
    private const string Accession = "0001062993-20-004026";
    private const string Cik = "1632932";

    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];
    private readonly CultureInfo _previousCulture;

    public Realtime13FIngestionOffQuarterPeriodTests(ParadeDbFixture fixture)
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

    // The header period is the one the parser trusts; here the filer's own period is off-quarter.
    private static string PrimaryDoc() =>
        """
            <edgarSubmission xmlns="http://www.sec.gov/edgar/thirteenffiler">
              <headerData><filerInfo>
                <filer><credentials><cik>0001632932</cik></credentials></filer>
                <periodOfReport>08-14-2020</periodOfReport>
              </filerInfo></headerData>
              <formData><coverPage>
                <reportCalendarOrQuarter>08-14-2020</reportCalendarOrQuarter>
                <isAmendment>false</isAmendment>
                <filingManager><name>OFF QUARTER FUND</name></filingManager>
                <form13FFileNumber>028-16583</form13FFileNumber>
              </coverPage></formData>
            </edgarSubmission>
            """;

    private static string InfoTable() =>
        """
            <informationTable xmlns="http://www.sec.gov/edgar/document/thirteenf/informationtable">
              <infoTable>
                <nameOfIssuer>APPLE INC</nameOfIssuer><titleOfClass>COM</titleOfClass>
                <cusip>037833100</cusip><value>9400</value>
                <shrsOrPrnAmt><sshPrnamt>81496</sshPrnamt><sshPrnamtType>SH</sshPrnamtType></shrsOrPrnAmt>
                <investmentDiscretion>SOLE</investmentDiscretion>
                <votingAuthority><Sole>81496</Sole><Shared>0</Shared><None>0</None></votingAuthority>
              </infoTable>
            </informationTable>
            """;

    [Fact]
    public async Task IngestRecentFilings_OffQuarterPeriodOfReport_RecordsTheFailureAndStoresNothing()
    {
        var entry = new EdgarDailyIndexEntry
        {
            FormType = "13F-HR",
            CompanyName = "OFF QUARTER FUND",
            Cik = Cik,
            DateFiled = new DateOnly(2020, 8, 14),
            AccessionNumber = Accession,
        };
        var edgar = Substitute.For<ISecEdgarClient>();
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
        var importService = new HoldingsImportService(
            scopeFactory,
            Substitute.For<ILogger<HoldingsImportService>>(),
            Options.Create(new WorkerOptions()),
            Substitute.For<IStockPriceProvider>(),
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
            today: new DateOnly(2020, 8, 15),
            lookbackDays: 1,
            minReportDate: new DateOnly(2020, 1, 1),
            CancellationToken.None
        );

        result.FilingsImported.Should().Be(0);
        using var verify = FreshContext();
        var failure = await verify.Set<HoldingsImportFailure>().SingleAsync();
        failure.AccessionNumber.Should().Be(Accession);
        failure.Reason.Should().Be(HoldingsImportFailureReason.OffQuarterReportPeriod);
        failure.ReportDate.Should().Be(new DateOnly(2020, 8, 14));
        failure.ResolvedAt.Should().BeNull();
        (await verify.Set<InstitutionalHolding>().AnyAsync()).Should().BeFalse();
        (await verify.Set<ProcessedFiling>().AnyAsync(p => p.AccessionNumber == Accession))
            .Should()
            .BeFalse();
    }
}
