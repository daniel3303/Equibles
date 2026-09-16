using Equibles.DelayedTrades.HostedService.Extensions;
using Equibles.Integrations.DelayedTrades;
using Equibles.Integrations.Euronext.DelayedTrades;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.UnitTests.DelayedTrades;

public class DelayedTradesWorkerRegistrationTests
{
    // The typed client must resolve, and the source seam must find it under the catalog's source key.
    [Fact]
    public void TheEuronextSource_ResolvesAsATypedClientAndThroughTheSeam()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDelayedTradesWorker();
        using var provider = services.BuildServiceProvider();

        provider
            .GetRequiredService<EuronextDelayedTradeSource>()
            .Should()
            .BeOfType<EuronextDelayedTradeSource>();
        provider
            .GetServices<IDelayedTradeSource>()
            .Should()
            .ContainSingle()
            .Which.SourceKey.Should()
            .Be("euronext");
    }
}
