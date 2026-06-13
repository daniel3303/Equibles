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
/// Production wedge (EquiblesCommercial#2510): one filing whose DATABASE import
/// throws killed the whole realtime sweep cycle and was never recorded as
/// processed, so every later cycle replayed the sweep and died at the same
/// filing — coverage froze at its date. Pin: a poisoned filing costs only its
/// own rows; the sweep continues, later filings import, and the poisoned
/// accession stays unrecorded so a later cycle can retry it after a fix. The
/// sweep result also reports the failed filing's date so the worker holds the
/// watermark back — without that, the filing falls out of the trailing
/// re-sweep window after 14 days and is lost forever (EquiblesCommercial#2850).
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class Realtime13DGIngestionPoisonedFilingTests : IAsyncLifetime
{
    private const string PoisonedAccession = "0001000000-25-000001";
    private const string CleanAccession = "0002000000-25-000001";
    private const string PoisonedCusip = "111111111";
    private const string CleanCusip = "222222222";

    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];
    private readonly CultureInfo _previousCulture;

    public Realtime13DGIngestionPoisonedFilingTests(ParadeDbFixture fixture)
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

    // The poisoned filing reports a class title longer than the 512-char column,
    // so its batch flush throws 22001 at the database — the production poison.
    private static string PrimaryDoc(string filerCik, string cusip, string classTitle) =>
        $"""
            <edgarSubmission xmlns="http://www.sec.gov/edgar/schedule13D" xmlns:com="http://www.sec.gov/edgar/common">
              <headerData>
                <submissionType>SCHEDULE 13D</submissionType>
                <filerInfo><filer><filerCredentials><cik>{filerCik}</cik></filerCredentials></filer></filerInfo>
              </headerData>
              <formData>
                <coverPageHeader>
                  <securitiesClassTitle>{classTitle}</securitiesClassTitle>
                  <dateOfEvent>04/29/2025</dateOfEvent>
                  <issuerInfo>
                    <issuerCIK>0001236275</issuerCIK>
                    <issuerCUSIP>{cusip}</issuerCUSIP>
                    <issuerName>Issuer Inc.</issuerName>
                  </issuerInfo>
                </coverPageHeader>
                <reportingPersons>
                  <reportingPersonInfo>
                    <reportingPersonCIK>{filerCik}</reportingPersonCIK>
                    <reportingPersonName>Filer {filerCik}</reportingPersonName>
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
    public async Task IngestRecentFilings_FirstFilingImportThrows_StillImportsLaterFilingAndLeavesPoisonUnrecorded()
    {
        using (var seed = FreshContext())
        {
            seed.Set<CommonStock>()
                .AddRange(
                    new CommonStock
                    {
                        Id = Guid.NewGuid(),
                        Ticker = "POISN",
                        Name = "Poisoned Issuer Inc.",
                        Cik = "0001236275",
                        Cusip = PoisonedCusip,
                    },
                    new CommonStock
                    {
                        Id = Guid.NewGuid(),
                        Ticker = "CLEAN",
                        Name = "Clean Issuer Inc.",
                        Cik = "0001236276",
                        Cusip = CleanCusip,
                    }
                );
            await seed.SaveChangesAsync();
        }

        // Sorted by accession within the day: the poisoned filing imports first.
        var poisoned = new EdgarDailyIndexEntry
        {
            FormType = "SCHEDULE 13D",
            CompanyName = "Poison Filer LP",
            Cik = "2059583",
            DateFiled = new DateOnly(2025, 5, 6),
            AccessionNumber = PoisonedAccession,
        };
        var clean = new EdgarDailyIndexEntry
        {
            FormType = "SCHEDULE 13D",
            CompanyName = "Clean Filer LP",
            Cik = "2059584",
            DateFiled = new DateOnly(2025, 5, 6),
            AccessionNumber = CleanAccession,
        };

        var overlongClassTitle = new string('X', 600);
        var edgar = Substitute.For<ISecEdgarClient>();
        edgar
            .GetDailyIndexForForms(
                Arg.Any<DateOnly>(),
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci =>
                ci.ArgAt<DateOnly>(0) == new DateOnly(2025, 5, 6) ? [poisoned, clean] : []
            );
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
            .Returns(ci =>
                Encoding.UTF8.GetBytes(
                    ci.ArgAt<string>(1) == PoisonedAccession
                        ? PrimaryDoc("0002059583", PoisonedCusip, overlongClassTitle)
                        : PrimaryDoc("0002059584", CleanCusip, "Common Stock")
                )
            );

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

        result.FilingsImported.Should().Be(1, "the clean filing must survive the poisoned one");
        result
            .EarliestFailedDate.Should()
            .Be(
                new DateOnly(2025, 5, 6),
                "a failed import must hold the watermark back so the filing is re-swept even after the trailing window passes"
            );

        using var verify = FreshContext();
        var cleanHoldings = await verify
            .Set<InstitutionalHolding>()
            .Where(h => h.Cusip == CleanCusip)
            .ToListAsync();
        cleanHoldings.Should().ContainSingle();

        var processed = await verify
            .Set<ProcessedFiling>()
            .Select(p => p.AccessionNumber)
            .ToListAsync();
        processed.Should().Contain(CleanAccession);
        processed
            .Should()
            .NotContain(
                PoisonedAccession,
                "an import-failed filing must stay unrecorded so a later cycle retries it"
            );
    }
}
