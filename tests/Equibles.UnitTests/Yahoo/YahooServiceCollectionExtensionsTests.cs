using Equibles.Core.Contracts;
using Equibles.Yahoo.HostedService.Extensions;
using Equibles.Yahoo.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.UnitTests.Yahoo;

public class YahooServiceCollectionExtensionsTests {
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
