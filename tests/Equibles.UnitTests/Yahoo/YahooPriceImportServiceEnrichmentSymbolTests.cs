using System.Reflection;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Yahoo.Contracts;
using Equibles.TestSupport;
using Equibles.Worker;
using Equibles.Yahoo.HostedService.Configuration;
using Equibles.Yahoo.HostedService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Yahoo;

/// <summary>
/// Pins the symbol the enrichment calls send to Yahoo. The chart calls already use the
/// venue-qualified provider symbol; the key-statistics and company-profile calls sent the bare
/// ticker, so a Paris AIR target fetched AAR Corp's NYSE figures. Both calls are the first
/// statement of their sync, so a scope factory that refuses to open proves the symbol without a
/// database. The catalog target builder must also flag the presentation listing as the
/// enrichment target and carry its attempt stamp, or every verified venue issuer would enrich on
/// every pass.
/// </summary>
public class YahooPriceImportServiceEnrichmentSymbolTests
{
    private static readonly MethodInfo SyncKeyStatistics =
        typeof(YahooPriceImportService).GetMethod(
            "SyncKeyStatistics",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;

    private static readonly MethodInfo SyncCompanyProfile =
        typeof(YahooPriceImportService).GetMethod(
            "SyncCompanyProfile",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;

    private static readonly MethodInfo BuildCatalogPriceTargets =
        typeof(YahooPriceImportService).GetMethod(
            "BuildCatalogPriceTargets",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;

    private static readonly MethodInfo EnrichTarget = typeof(YahooPriceImportService).GetMethod(
        "EnrichTarget",
        BindingFlags.NonPublic | BindingFlags.Instance
    )!;

    private static PriceSeriesTarget Paris(bool isPrimary = true) =>
        new(
            "AIR",
            Guid.NewGuid(),
            Guid.NewGuid(),
            IsPrimary: isPrimary,
            MarketCountryCode: "FR",
            MarketIdentifierCode: "XPAR",
            Isin: "NL0000235190",
            TradingCurrency: "EUR",
            QuoteUnitMultiplier: 1m
        );

    private static PriceSeriesTarget Zurich() =>
        new(
            "NESN",
            Guid.NewGuid(),
            Guid.NewGuid(),
            IsPrimary: true,
            MarketCountryCode: "CH",
            MarketIdentifierCode: "XSWX",
            Isin: "CH0038863350",
            TradingCurrency: "CHF",
            QuoteUnitMultiplier: 1m
        );

    private static (YahooPriceImportService Sut, IYahooFinanceClient Client) BuildSut(
        IServiceScopeFactory scopeFactory = null
    )
    {
        if (scopeFactory == null)
        {
            scopeFactory = Substitute.For<IServiceScopeFactory>();
            scopeFactory
                .CreateScope()
                .Returns(_ => throw new InvalidOperationException("no database in this test"));
        }
        var client = Substitute.For<IYahooFinanceClient>();
        var sut = new YahooPriceImportService(
            scopeFactory,
            Substitute.For<ILogger<YahooPriceImportService>>(),
            client,
            new TickerMapService(scopeFactory),
            new ErrorReporter(scopeFactory, Substitute.For<ILogger<ErrorReporter>>()),
            Options.Create(new WorkerOptions()),
            Options.Create(new YahooPriceScraperOptions())
        );
        return (sut, client);
    }

    private static Task Invoke(MethodInfo method, YahooPriceImportService sut, object target) =>
        (Task)method.Invoke(sut, [target, CancellationToken.None])!;

    [Fact]
    public async Task KeyStatistics_AreRequestedByTheCatalogSymbol()
    {
        var (sut, client) = BuildSut();

        var act = () => Invoke(SyncKeyStatistics, sut, Paris());

        await act.Should().ThrowAsync<InvalidOperationException>("the write needs the database");
        await client.Received(1).GetKeyStatistics("AIR.PA");
        await client.DidNotReceive().GetKeyStatistics("AIR");
    }

    [Fact]
    public async Task CompanyProfile_IsRequestedByTheCatalogSymbol()
    {
        var (sut, client) = BuildSut();

        await Invoke(SyncCompanyProfile, sut, Paris());

        await client.Received(1).GetCompanyProfile("AIR.PA");
        await client.DidNotReceive().GetCompanyProfile("AIR");
    }

    [Fact]
    public async Task UsListing_KeepsItsBareTicker()
    {
        var (sut, client) = BuildSut();
        var target = new PriceSeriesTarget("AIR", Guid.NewGuid(), Guid.NewGuid(), IsPrimary: true);

        await Invoke(SyncCompanyProfile, sut, target);

        await client.Received(1).GetCompanyProfile("AIR");
    }

    [Fact]
    public async Task ListingOutsideTheCatalog_IsNeverRequested()
    {
        var (sut, client) = BuildSut();

        await Invoke(SyncKeyStatistics, sut, Zurich());
        await Invoke(SyncCompanyProfile, sut, Zurich());

        await client.DidNotReceive().GetKeyStatistics(Arg.Any<string>());
        await client.DidNotReceive().GetCompanyProfile(Arg.Any<string>());
    }

    [Fact]
    public async Task StampFailure_CostsTheTarget_NotTheBatch()
    {
        // Outside the catalog neither Yahoo call runs, so the stamp is the only statement that
        // touches the database; its failure must stay inside EnrichTarget so the loop continues.
        var (sut, client) = BuildSut();

        var act = () => Invoke(EnrichTarget, sut, Zurich());

        await act.Should().NotThrowAsync();
        await client.DidNotReceive().GetKeyStatistics(Arg.Any<string>());
    }

    [Fact]
    public async Task CatalogTargets_FlagThePresentationListing_AndCarryItsAttemptStamp()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), new InMemoryDatabaseRoot())
            .EnableServiceProviderCaching(false)
            .Options;
        var stamped = new DateTime(2026, 9, 15, 20, 0, 0, DateTimeKind.Utc);
        EquityIssuer issuer = EquityIssuerSeed.Create(
            Ticker: "AIR",
            Name: "Airbus SE",
            Isin: "NL0000235190",
            MarketCountryCode: "FR",
            MarketIdentifierCode: "XPAR",
            IdentityState: EquityIdentityState.Verified,
            TradingCurrency: "EUR",
            QuoteUnitMultiplier: 1m,
            YahooEnrichmentAttemptedAt: stamped
        );
        // A second verified Paris line of the same issuer that is not its presentation: priced,
        // never enriched.
        var secondarySecurity = new EquitySecurity
        {
            Issuer = issuer,
            EquityIssuerId = issuer.Id,
            Isin = "NL0000235191",
        };
        var secondary = new EquityListing
        {
            Security = secondarySecurity,
            EquitySecurityId = secondarySecurity.Id,
            Ticker = "AIRP",
            MarketCountryCode = "FR",
            MarketIdentifierCode = "XPAR",
            IdentityState = EquityIdentityState.Verified,
            TradingCurrency = "EUR",
            QuoteUnitMultiplier = 1m,
            YahooEnrichmentAttemptedAt = stamped,
        };
        secondarySecurity.Listings.Add(secondary);
        issuer.Securities.Add(secondarySecurity);
        await using (var seed = NewContext(options))
        {
            seed.Set<EquityIssuer>().Add(issuer);
            await seed.SaveChangesAsync();
        }
        var services = new ServiceCollection();
        services.AddScoped(_ => NewContext(options));
        services.AddScoped<EquityIssuerRepository>();
        var (sut, _) = BuildSut(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>()
        );

        var targets = await (Task<List<PriceSeriesTarget>>)
            BuildCatalogPriceTargets.Invoke(sut, [CancellationToken.None])!;

        var presentation = targets.Should().ContainSingle(t => t.Ticker == "AIR").Subject;
        presentation.IsPrimary.Should().BeTrue();
        presentation.YahooEnrichmentAttemptedAt.Should().Be(stamped);
        presentation.ProviderSymbol.Should().Be("AIR.PA");
        var other = targets.Should().ContainSingle(t => t.Ticker == "AIRP").Subject;
        other.IsPrimary.Should().BeFalse();
        other.YahooEnrichmentAttemptedAt.Should().BeNull();
        YahooPriceImportService
            .SelectEnrichmentBatch(targets, stamped.AddHours(1), TimeSpan.FromHours(24), 10)
            .Targets.Should()
            .BeEmpty("a presentation listing stamped inside the interval is not due");
        YahooPriceImportService
            .SelectEnrichmentBatch(targets, stamped.AddHours(25), TimeSpan.FromHours(24), 10)
            .Targets.Should()
            .ContainSingle()
            .Which.Ticker.Should()
            .Be("AIR");
    }

    private static EquiblesFinancialDbContext NewContext(
        DbContextOptions<EquiblesFinancialDbContext> options
    )
    {
        var ctx = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[] { new CommonStocksModuleConfiguration() }
        );
        ctx.Database.EnsureCreated();
        return ctx;
    }
}
