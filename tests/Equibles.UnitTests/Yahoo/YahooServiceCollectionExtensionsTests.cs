using Equibles.Core.Contracts;
using Equibles.Integrations.Yahoo.Contracts;
using Equibles.Yahoo.HostedService;
using Equibles.Yahoo.HostedService.Extensions;
using Equibles.Yahoo.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Equibles.UnitTests.Yahoo;

public class YahooServiceCollectionExtensionsTests {
    [Fact]
    public void AddYahooWorker_AutoWiresIYahooFinanceClientFromIntegrationsAssembly() {
        // Sibling to `AddYahooWorker_RegistersIStockPriceProviderAsYahooStockPriceProviderScoped`.
        // The existing pin covers the EXPLICIT `services.AddScoped<IStockPriceProvider, ...>`
        // binding — the cross-module contract the Holdings valuation
        // pipeline depends on. This pin covers the OTHER half of the
        // composition: the auto-wire scan of `Equibles.Integrations.Yahoo`
        // that registers IYahooFinanceClient → YahooFinanceClient (the
        // HTTP client behind YahooPriceImportService's daily-close
        // downloads and YahooStockPriceProvider's per-ticker lookups).
        //
        // AddYahooWorker has TWO auto-wire calls — one for the
        // hosted-service assembly (covered indirectly by the existing
        // explicit binding pin), one for the Integrations.Yahoo
        // assembly. This pin protects the Integrations.Yahoo scan
        // specifically:
        //   • A refactor that drops the second AutoWireServicesFrom
        //     (under the false intuition that "the explicit
        //     IStockPriceProvider binding is enough") would compile,
        //     pass the existing IStockPriceProvider pin, and silently
        //     leave IYahooFinanceClient unresolvable.
        //     YahooPriceScraperWorker.DoWork's first iteration would
        //     throw InvalidOperationException; the worker's
        //     BaseScraperWorker catch-all routes it to ErrorReporter
        //     and the daily-price ingest silently halts.
        //   • The complementary risk: pointing the second scan at the
        //     wrong assembly (typing `<Equibles.Integrations.Sec.SecEdgarClient>`
        //     during a copy-paste refactor) would also pass the
        //     IStockPriceProvider pin while registering the wrong
        //     client — IYahooFinanceClient would not appear in the
        //     descriptor list. This pin catches both via the explicit
        //     `typeof(IYahooFinanceClient)` lookup.
        //
        // Pin: `services.Should().Contain(d => d.ServiceType ==
        // typeof(IYahooFinanceClient))`. YahooFinanceClient is
        // registered with `[Service(ServiceLifetime.Scoped,
        // typeof(IYahooFinanceClient))]`, so the descriptor's
        // ServiceType IS the interface.
        var services = new ServiceCollection();

        services.AddYahooWorker();

        services.Should().Contain(d => d.ServiceType == typeof(IYahooFinanceClient));
    }

    [Fact]
    public void AddYahooWorker_RegistersIStockPriceProviderAsYahooStockPriceProviderScoped() {
        // AddYahooWorker is the host's seam into auto-wiring for daily price
        // ingest AND it explicitly binds the cross-module IStockPriceProvider
        // contract to YahooStockPriceProvider. The Holdings valuation pipeline
        // resolves IStockPriceProvider to compute portfolio values — a
        // regression that drops the explicit Scoped registration would leave
        // valuation NRE-ing as soon as Holdings tries to look up a price.
        // Pin both the binding shape (interface → implementation) and the
        // lifetime so a refactor that downgrades or swaps the provider
        // surfaces here.
        var services = new ServiceCollection();

        services.AddYahooWorker();

        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IStockPriceProvider));
        descriptor.Should().NotBeNull();
        descriptor.ImplementationType.Should().Be(typeof(YahooStockPriceProvider));
        descriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddYahooWorker_RegistersYahooPriceScraperWorkerAsIHostedService() {
        // Third sibling in the AddYahooWorker registration family. The two
        // existing pins cover the AutoWires scan (IYahooFinanceClient) and
        // the explicit IStockPriceProvider binding (the cross-module contract
        // for Holdings valuation). This pin covers the structurally distinct
        // `services.AddHostedService<YahooPriceScraperWorker>()` registration
        // that wires the worker into the .NET generic host so it starts at
        // boot.
        //
        // The risk this catches is asymmetric and unreachable from the two
        // existing pins:
        //   • A regression that drops `AddHostedService<YahooPriceScraperWorker>()`
        //     would compile cleanly, pass both the IYahooFinanceClient AutoWires
        //     pin AND the IStockPriceProvider explicit-binding pin, and silently
        //     disable the daily Yahoo Finance price-fetch pipeline at startup.
        //     The application boots, every dependency resolves, but no
        //     IHostedService implementation of YahooPriceScraperWorker is
        //     enumerated — daily close prices never refresh, the
        //     CommonStocks.LastPrice column drifts stale, and the holdings
        //     valuation pipeline starts producing values against
        //     yesterday-and-earlier prices indefinitely.
        //   • A regression that downgrades the registration to AddScoped or
        //     AddSingleton would register the worker as a resolvable service
        //     but NOT enumerate it as IHostedService — same silent failure.
        //
        // The Yahoo pipeline is particularly load-bearing because:
        //   • It's the OSS source of stock prices for Holdings valuation
        //     (the IStockPriceProvider contract is explicitly bound to
        //     YahooStockPriceProvider for that reason). A dropped registration
        //     stops the price-cache refresh while leaving the lookup
        //     INTERFACE wired — every Holdings valuation continues querying
        //     the price cache, but every entry returns the value from the
        //     last successful refresh, producing portfolio valuations that
        //     look reasonable but drift further from reality each day.
        //   • Stock prices change every trading day; the staleness is
        //     immediately operator-visible on any chart that compares
        //     "yesterday's value" to "today's market" — but only if someone
        //     is watching that specific comparison.
        //
        // This pin mirrors the AddSecWorker / AddHoldingsWorker / AddCftcWorker /
        // AddCongressWorker / AddFredWorker / AddFinraWorker hosted-service pin
        // family pattern. YahooPriceScraperWorker is the SINGLE hosted service
        // AddYahooWorker registers — no further siblings in this family.
        //
        // Lookup pattern: filter IHostedService descriptors and assert one has
        // ImplementationType == typeof(YahooPriceScraperWorker).
        var services = new ServiceCollection();

        services.AddYahooWorker();

        var hostedServiceDescriptors = services
            .Where(d => d.ServiceType == typeof(IHostedService))
            .ToList();

        hostedServiceDescriptors.Should().Contain(
            d => d.ImplementationType == typeof(YahooPriceScraperWorker),
            "AddHostedService<YahooPriceScraperWorker>() must register the worker as IHostedService so the daily Yahoo Finance price refresh runs at startup");
    }
}
