using Equibles.Core.AutoWiring;
using Equibles.Holdings.BusinessLogic;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// The hosts wire the Holdings business logic by attribute scan; the series cache only pays off
/// when every loader the scan resolves receives the one singleton.
/// </summary>
public class BacktestPriceLoaderWiringTests
{
    [Fact]
    public void AutoWiring_RegistersOneSeriesCacheForEveryLoader()
    {
        var services = new ServiceCollection();
        services.AutoWireServicesFrom<BacktestPriceLoader>();

        services
            .Should()
            .ContainSingle(descriptor => descriptor.ServiceType == typeof(BacktestPriceSeriesCache))
            .Which.Lifetime.Should()
            .Be(ServiceLifetime.Singleton);
        services
            .Should()
            .Contain(descriptor => descriptor.ServiceType == typeof(BacktestPriceLoader));
        typeof(BacktestPriceLoader)
            .GetConstructors()
            .Should()
            .ContainSingle("a second constructor would let the container pick a private cache")
            .Which.GetParameters()
            .Select(parameter => parameter.ParameterType)
            .Should()
            .Contain(typeof(BacktestPriceSeriesCache));

        using var provider = services.BuildServiceProvider();
        provider
            .GetRequiredService<BacktestPriceSeriesCache>()
            .Should()
            .BeSameAs(provider.GetRequiredService<BacktestPriceSeriesCache>());
    }
}
