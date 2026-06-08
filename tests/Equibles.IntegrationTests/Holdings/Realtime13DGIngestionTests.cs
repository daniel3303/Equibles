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
/// End-to-end Schedule 13D ingestion: a daily-index entry is parsed from its XML
/// primary_doc, projected into the shared import pipeline, and lands as an
/// InstitutionalHolding attributed to the lead filer with the right FilingType,
/// share count, voting power and percent of class.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class Realtime13DGIngestionTests : IAsyncLifetime
{
    private const string FilerCik = "2059583";
    private const string IssuerCusip = "82846H405";

    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];
    private readonly CultureInfo _previousCulture;

    public Realtime13DGIngestionTests(ParadeDbFixture fixture)
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

    // A 13D where one reporting person (the GP) carries the filer's CIK, so it is
    // the lead position; a second no-CIK person reports a smaller overlapping stake.
    private static string PrimaryDoc() =>
        """
            <edgarSubmission xmlns="http://www.sec.gov/edgar/schedule13D" xmlns:com="http://www.sec.gov/edgar/common">
              <headerData>
                <submissionType>SCHEDULE 13D</submissionType>
                <filerInfo><filer><filerCredentials><cik>0002059583</cik></filerCredentials></filer></filerInfo>
              </headerData>
              <formData>
                <coverPageHeader>
                  <securitiesClassTitle>Common Stock</securitiesClassTitle>
                  <dateOfEvent>04/29/2025</dateOfEvent>
                  <issuerInfo>
                    <issuerCIK>0001236275</issuerCIK>
                    <issuerCUSIP>82846H405</issuerCUSIP>
                    <issuerName>QXO, Inc.</issuerName>
                  </issuerInfo>
                </coverPageHeader>
                <reportingPersons>
                  <reportingPersonInfo>
                    <reportingPersonNoCIK>Y</reportingPersonNoCIK>
                    <reportingPersonName>Affinity Partners Fund I LP</reportingPersonName>
                    <soleVotingPower>0</soleVotingPower><sharedVotingPower>164310</sharedVotingPower>
                    <soleDispositivePower>0</soleDispositivePower><sharedDispositivePower>164310</sharedDispositivePower>
                    <aggregateAmountOwned>164310</aggregateAmountOwned>
                    <percentOfClass>0.03</percentOfClass>
                    <typeOfReportingPerson>PN</typeOfReportingPerson>
                  </reportingPersonInfo>
                  <reportingPersonInfo>
                    <reportingPersonCIK>0002059583</reportingPersonCIK>
                    <reportingPersonName>Affinity Partners GP LP</reportingPersonName>
                    <soleVotingPower>0</soleVotingPower><sharedVotingPower>32671542</sharedVotingPower>
                    <soleDispositivePower>0</soleDispositivePower><sharedDispositivePower>32671542</sharedDispositivePower>
                    <aggregateAmountOwned>32671542</aggregateAmountOwned>
                    <percentOfClass>6.3</percentOfClass>
                    <typeOfReportingPerson>PN</typeOfReportingPerson>
                  </reportingPersonInfo>
                </reportingPersons>
              </formData>
            </edgarSubmission>
            """;

    private static EdgarDailyIndexEntry Entry() =>
        new()
        {
            FormType = "SCHEDULE 13D",
            CompanyName = "Affinity Partners GP LP",
            Cik = FilerCik,
            DateFiled = new DateOnly(2025, 5, 6),
            AccessionNumber = "0001140361-25-017533",
        };

    [Fact]
    public async Task IngestRecentFilings_Real13D_LandsLeadFilerPositionWithPercentOfClass()
    {
        using (var seed = FreshContext())
        {
            seed.Set<CommonStock>()
                .Add(
                    new CommonStock
                    {
                        Id = Guid.NewGuid(),
                        Ticker = "QXO",
                        Name = "QXO, Inc.",
                        Cik = "0001236275",
                        Cusip = IssuerCusip,
                    }
                );
            await seed.SaveChangesAsync();
        }

        var edgar = Substitute.For<ISecEdgarClient>();
        edgar
            .GetDailyIndexForForms(
                Arg.Any<DateOnly>(),
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci => ci.ArgAt<DateOnly>(0) == new DateOnly(2025, 5, 6) ? [Entry()] : []);
        edgar
            .GetFilingArtifactNames(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(["primary_doc.xml"]);
        edgar
            .GetDocumentFileBytes(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Encoding.UTF8.GetBytes(PrimaryDoc()));

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
                    dict[(id, date)] = 10m;
                return Task.FromResult(dict);
            });

        var importService = new HoldingsImportService(
            scopeFactory,
            Substitute.For<ILogger<HoldingsImportService>>(),
            Options.Create(new WorkerOptions()),
            prices,
            Substitute.For<MassTransit.IBus>()
        );
        var ingestion = new Realtime13DGIngestionService(
            edgar,
            new Filing13DGXmlParser(),
            new Realtime13DGArchiveBuilder(),
            importService,
            scopeFactory,
            Substitute.For<ILogger<Realtime13DGIngestionService>>()
        );

        var result = await ingestion.IngestRecentFilings(
            today: new DateOnly(2025, 5, 6),
            lookbackDays: 1,
            minReportDate: new DateOnly(2024, 12, 18),
            CancellationToken.None
        );

        result.FilingsImported.Should().Be(1);

        using var verify = FreshContext();
        var holdings = await verify
            .Set<InstitutionalHolding>()
            .Include(h => h.InstitutionalHolder)
            .Where(h => h.Cusip == IssuerCusip)
            .ToListAsync();

        // One row, attributed to the lead filer (the GP that carries the filer CIK),
        // not one row per overlapping reporting person.
        holdings.Should().ContainSingle();
        var holding = holdings[0];
        holding.FilingType.Should().Be(FilingType.Schedule13D);
        holding.Shares.Should().Be(32_671_542);
        holding.PercentOfClass.Should().Be(6.3m);
        holding.VotingAuthShared.Should().Be(32_671_542);
        holding.IsAmendment.Should().BeFalse();
        holding.ReportDate.Should().Be(new DateOnly(2025, 4, 29));
        holding.InstitutionalHolder.Cik.Should().Be(FilerCik);
    }
}
