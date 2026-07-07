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
/// Pins the foreign-private-issuer guard in FinancialFactsImportService.UpdateSharesOutstanding.
/// A 20-F/40-F filer reports its cover-page count in ordinary shares — a different unit from the
/// US-listed ADR the Yahoo importer maintains — so the financial-facts importer must NOT overwrite
/// the stored ADR share base with it. Without the guard the importer wrote e.g. Latam Airlines'
/// ~574B ordinary shares over Yahoo's correct ADR count, off by the ADR ratio and blowing up every
/// ratio built on it (short interest % of shares, market cap, ownership %). Domestic filers still
/// have their share count refreshed from the cover page as before.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FinancialFactsImportServiceForeignIssuerSharesTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];

    public FinancialFactsImportServiceForeignIssuerSharesTests(ParadeDbFixture fixture) =>
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
            Cik = 320193,
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
                                    Start = new DateOnly(2023, 1, 1),
                                    End = new DateOnly(2023, 12, 31),
                                    Val = 100m,
                                    Accn = "0000320193-24-000001",
                                    Fy = 2023,
                                    Fp = "FY",
                                    Form = "10-K",
                                    Filed = new DateOnly(2024, 1, 15),
                                    Frame = "CY2023",
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
        secEdgarClient.GetCompanyFacts("0000320193").Returns(RevenueFacts());
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

    private async Task<CommonStock> Seed(long sharesOutstanding, string ticker)
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = ticker,
            Cik = "0000320193",
            SharesOutStanding = sharesOutstanding,
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

    [Fact]
    public async Task Import_ForeignPrivateIssuer_LeavesStoredAdrShareCountUntouched()
    {
        // The ADR count the Yahoo importer already maintains for this 20-F filer.
        var stock = await Seed(606_407_693, "LTM");

        var sharesProvider = Substitute.For<ISharesOutstandingProvider>();
        // The EDGAR ordinary-share cover-page count — the wrong unit for the US-listed ADR.
        sharesProvider
            .GetCurrentSharesOutstanding(Arg.Any<CommonStock>(), Arg.Any<CancellationToken>())
            .Returns(574_215_983_709L);
        sharesProvider
            .IsForeignPrivateIssuer(Arg.Any<CommonStock>(), Arg.Any<CancellationToken>())
            .Returns(true);

        await BuildService(sharesProvider).Import(stock, CancellationToken.None);

        (await StoredShares(stock.Id))
            .Should()
            .Be(606_407_693, "a 20-F filer's ordinary count must not overwrite the ADR share base");
    }

    [Fact]
    public async Task Import_DomesticIssuer_RefreshesSharesOutstandingFromCoverPage()
    {
        // Same harness, domestic filer: the cover-page refresh still runs and corrects the count.
        var stock = await Seed(1, "AAPL");

        var sharesProvider = Substitute.For<ISharesOutstandingProvider>();
        sharesProvider
            .GetCurrentSharesOutstanding(Arg.Any<CommonStock>(), Arg.Any<CancellationToken>())
            .Returns(14_687_356_000L);
        sharesProvider
            .IsForeignPrivateIssuer(Arg.Any<CommonStock>(), Arg.Any<CancellationToken>())
            .Returns(false);

        await BuildService(sharesProvider).Import(stock, CancellationToken.None);

        (await StoredShares(stock.Id)).Should().Be(14_687_356_000L);
    }
}
