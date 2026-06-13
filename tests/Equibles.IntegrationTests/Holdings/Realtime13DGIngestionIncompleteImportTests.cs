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
/// Production data loss (EquiblesCommercial#2850): the import service returns
/// IsComplete=false for the NoTrackedStocks outcome — its own contract says
/// "leave the data set unprocessed so a later cycle backfills it once CUSIPs
/// exist" — but the realtime sweep recorded the accession in ProcessedFiling
/// anyway, so the filing was never looked at again. Pin: an incomplete import
/// stays unrecorded and uncounted, keeping the filing retryable.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class Realtime13DGIngestionIncompleteImportTests : IAsyncLifetime
{
    private const string Accession = "0003000000-26-000001";
    private const string UntrackedCusip = "999999999";

    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];
    private readonly CultureInfo _previousCulture;

    public Realtime13DGIngestionIncompleteImportTests(ParadeDbFixture fixture)
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
        $"""
            <edgarSubmission xmlns="http://www.sec.gov/edgar/schedule13D" xmlns:com="http://www.sec.gov/edgar/common">
              <headerData>
                <submissionType>SCHEDULE 13D</submissionType>
                <filerInfo><filer><filerCredentials><cik>0002059585</cik></filerCredentials></filer></filerInfo>
              </headerData>
              <formData>
                <coverPageHeader>
                  <securitiesClassTitle>Common Stock</securitiesClassTitle>
                  <dateOfEvent>04/29/2025</dateOfEvent>
                  <issuerInfo>
                    <issuerCIK>0001236277</issuerCIK>
                    <issuerCUSIP>{UntrackedCusip}</issuerCUSIP>
                    <issuerName>Unseeded Issuer Inc.</issuerName>
                  </issuerInfo>
                </coverPageHeader>
                <reportingPersons>
                  <reportingPersonInfo>
                    <reportingPersonCIK>0002059585</reportingPersonCIK>
                    <reportingPersonName>Untracked Filer LP</reportingPersonName>
                    <soleVotingPower>1000</soleVotingPower><sharedVotingPower>0</sharedVotingPower>
                    <soleDispositivePower>1000</soleDispositivePower><sharedDispositivePower>0</sharedDispositivePower>
                    <aggregateAmountOwned>1000</aggregateAmountOwned>
                    <percentOfClass>6.3</percentOfClass>
                    <typeOfReportingPerson>PN</typeOfReportingPerson>
                  </reportingPersonInfo>
                </reportingPersons>
              </formData>
            </edgarSubmission>
            """;

    [Fact]
    public async Task IngestRecentFilings_ImportReportsIncomplete_LeavesAccessionUnrecordedForRetry()
    {
        // No CommonStock is seeded for the filing's CUSIP, so the import maps no
        // tracked stock and returns IsComplete=false ("retry once CUSIPs exist").
        var entry = new EdgarDailyIndexEntry
        {
            FormType = "SCHEDULE 13D",
            CompanyName = "Untracked Filer LP",
            Cik = "2059585",
            DateFiled = new DateOnly(2025, 5, 6),
            AccessionNumber = Accession,
        };

        var edgar = Substitute.For<ISecEdgarClient>();
        edgar
            .GetDailyIndexForForms(
                Arg.Any<DateOnly>(),
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci => ci.ArgAt<DateOnly>(0) == new DateOnly(2025, 5, 6) ? [entry] : []);
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
            .Returns(Task.FromResult(new Dictionary<(Guid, DateOnly), decimal>()));

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

        result.FilingsImported.Should().Be(0, "an incomplete import is not an import");
        result
            .EarliestFailedDate.Should()
            .BeNull(
                "an incomplete import must not hold the watermark back — an issuer that never seeds a CUSIP would wedge the sweep"
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
    }
}
