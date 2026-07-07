using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models.Responses;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.FinancialFacts.BusinessLogic;
using Equibles.Sec.FinancialFacts.HostedService.Services;
using Equibles.Sec.FinancialFacts.Repositories;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Sibling to <see cref="FinancialFactsImportServiceForeignIssuerSharesTests"/>, pinning the
/// unit-mismatch guard in FinancialFactsImportService.UpdateSharesOutstanding for issuers the
/// form-based FPI guard can't see: a former foreign private issuer that lost FPI status files
/// 10-K/10-Q while its US listing is still an ADS, so its cover page counts ordinary shares —
/// AKTX: 91.57B ordinary against ~2.5M listed ADSs (80,000 ordinary per ADS). Writing that count
/// would put the share count back on the ordinary base while the market cap the Yahoo importer
/// maintains stays on the ADS base, re-breaking the pair every facts cycle. A stored count that is
/// itself garbage (nominal placeholder, or one whose implied price is nothing a listing could
/// trade at) must still be repaired by the cover-page figure.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FinancialFactsImportServiceAdsShareBasisTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];

    public FinancialFactsImportServiceAdsShareBasisTests(ParadeDbFixture fixture) =>
        _fixture = fixture;

    public Task InitializeAsync() => _fixture.ResetAsync();

    public Task DisposeAsync()
    {
        foreach (var ctx in _contexts)
            ctx.Dispose();
        return Task.CompletedTask;
    }

    private EquiblesFinancialDbContext FreshContext()
    {
        var ctx = _fixture.CreateDbContext();
        _contexts.Add(ctx);
        return ctx;
    }

    // Every scope shares the one caller-configured shares provider so the test controls the
    // GetCurrentSharesOutstanding / IsForeignPrivateIssuer answers the import path sees.
    private IServiceScopeFactory CreateScopeFactory(ISharesOutstandingProvider sharesProvider)
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory
            .CreateScope()
            .Returns(_ =>
            {
                var ctx = FreshContext();
                var sp = Substitute.For<IServiceProvider>();
                sp.GetService(typeof(EquiblesFinancialDbContext)).Returns(ctx);
                sp.GetService(typeof(FinancialConceptRepository))
                    .Returns(new FinancialConceptRepository(ctx));
                sp.GetService(typeof(FinancialFactsSyncStatusRepository))
                    .Returns(new FinancialFactsSyncStatusRepository(ctx));
                sp.GetService(typeof(DocumentRepository)).Returns(new DocumentRepository(ctx));
                sp.GetService(typeof(CommonStockRepository))
                    .Returns(new CommonStockRepository(ctx));
                sp.GetService(typeof(ISharesOutstandingProvider)).Returns(sharesProvider);
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });
        return scopeFactory;
    }

    // A minimal, single-value payload: one ParsedFact so the import reaches the share-count refresh
    // without exercising the within-batch dedup path.
    private static CompanyFactsResponse RevenueFacts() =>
        new()
        {
            Cik = 1541157,
            EntityName = "Issuer",
            Facts = new()
            {
                ["us-gaap"] = new()
                {
                    ["Revenues"] = new CompanyFactConcept
                    {
                        Label = "Revenues",
                        Units = new()
                        {
                            ["USD"] =
                            [
                                new CompanyFactValue
                                {
                                    Start = new DateOnly(2025, 1, 1),
                                    End = new DateOnly(2025, 12, 31),
                                    Val = 100m,
                                    Accn = "0001541157-26-000001",
                                    Fy = 2025,
                                    Fp = "FY",
                                    Form = "10-K",
                                    Filed = new DateOnly(2026, 3, 30),
                                    Frame = "CY2025",
                                },
                            ],
                        },
                    },
                },
            },
        };

    private FinancialFactsImportService BuildService(ISharesOutstandingProvider sharesProvider)
    {
        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        secEdgarClient.GetCompanyFacts("0001541157").Returns(RevenueFacts());
        var errorReporter = Substitute.For<ErrorReporter>(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<ErrorReporter>>()
        );
        return new FinancialFactsImportService(
            CreateScopeFactory(sharesProvider),
            secEdgarClient,
            Substitute.For<ILogger<FinancialFactsImportService>>(),
            errorReporter
        );
    }

    private async Task<CommonStock> Seed(
        long sharesOutstanding,
        double marketCapitalization,
        string ticker
    )
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = ticker,
            Cik = "0001541157",
            SharesOutStanding = sharesOutstanding,
            MarketCapitalization = marketCapitalization,
        };
        await using var seed = _fixture.CreateDbContext();
        seed.Set<CommonStock>().Add(stock);
        await seed.SaveChangesAsync(CancellationToken.None);
        return stock;
    }

    private async Task<long> StoredShares(Guid stockId)
    {
        await using var verify = _fixture.CreateDbContext();
        var tracked = await verify.Set<CommonStock>().FirstAsync(s => s.Id == stockId);
        return tracked.SharesOutStanding;
    }

    private static ISharesOutstandingProvider DomesticProviderReturning(long coverPageCount)
    {
        var sharesProvider = Substitute.For<ISharesOutstandingProvider>();
        sharesProvider
            .GetCurrentSharesOutstanding(Arg.Any<CommonStock>(), Arg.Any<CancellationToken>())
            .Returns(coverPageCount);
        sharesProvider
            .IsForeignPrivateIssuer(Arg.Any<CommonStock>(), Arg.Any<CancellationToken>())
            .Returns(false);
        return sharesProvider;
    }

    [Fact]
    public async Task Import_DomesticAdsIssuer_LeavesStoredListedSecurityCountUntouched()
    {
        // AKTX steady state after the Yahoo importer healed the pair: ~2.5M ADSs against a ~$27M
        // market cap (implied ~$10.90 — a real quote). The 10-K cover page counts 91.57B ordinary
        // shares; a domestic-form filer, so the FPI guard is blind and only the unit-mismatch
        // guard stands between the ordinary count and the stored ADS base.
        var stock = await Seed(2_477_000, 27_000_000d, "AKTX");

        await BuildService(DomesticProviderReturning(91_567_009_533L))
            .Import(stock, CancellationToken.None);

        (await StoredShares(stock.Id))
            .Should()
            .Be(
                2_477_000,
                "a domestic filer's ordinary-share cover-page count must not overwrite the listed ADS base"
            );
    }

    [Fact]
    public async Task Import_NominalPlaceholderCount_IsStillRepairedFromCoverPage()
    {
        // A stock pinned to a 1-share nominal placeholder alongside a real market cap: the
        // implied price (billions per share) proves the stored count is NOT on the listed basis,
        // so the plausibility gate keeps the guard out and the cover-page repair proceeds even
        // though the two counts diverge far beyond the unit-mismatch threshold.
        var stock = await Seed(1, 2_000_000_000d, "SHEL");

        await BuildService(DomesticProviderReturning(14_687_356_000L))
            .Import(stock, CancellationToken.None);

        (await StoredShares(stock.Id)).Should().Be(14_687_356_000L);
    }

    [Fact]
    public async Task Import_SameUnitDivergence_RefreshesSharesFromCoverPage()
    {
        // An ordinary refresh: stored pair on the listed basis (implied ~$3/share) and a
        // cover-page count 5% away — a buyback landing on EDGAR before Yahoo catches up. Same
        // unit, so the guard stays out and the authoritative count is written.
        var stock = await Seed(14_000_000_000, 42_000_000_000d, "AAPL");

        await BuildService(DomesticProviderReturning(14_687_356_000L))
            .Import(stock, CancellationToken.None);

        (await StoredShares(stock.Id)).Should().Be(14_687_356_000L);
    }
}
