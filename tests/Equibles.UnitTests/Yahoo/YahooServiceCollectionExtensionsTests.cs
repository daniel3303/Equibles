using Equibles.Core.Contracts;
using Equibles.Integrations.Yahoo.Contracts;
using Equibles.Yahoo.HostedService.Extensions;
using Equibles.Yahoo.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;

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
}
