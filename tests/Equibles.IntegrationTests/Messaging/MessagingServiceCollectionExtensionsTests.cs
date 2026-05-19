using Equibles.Holdings.HostedService.Consumers;
using Equibles.Messaging.Extensions;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Equibles.IntegrationTests.Messaging;

public class MessagingServiceCollectionExtensionsTests
{
    private const string TransportConnection =
        "Host=localhost;Database=equibles;Username=postgres;Password=postgres";

    // AddMessaging is the Worker/Web/Mcp messaging composition root: it must wire
    // MassTransit, bind SqlTransportOptions from ConnectionStrings:TransportConnection,
    // and auto-register every [Consumer] IConsumer<T> via the loaded-assembly scan.
    // Touching StockCusipChangedConsumer forces its assembly to load so the scan
    // (and IsSystemAssembly filtering) actually runs, as in a real host.
    [Fact]
    public void AddMessaging_WithRunMigration_WiresBusBindsTransportOptionsAndAutoRegistersConsumers()
    {
        _ = typeof(StockCusipChangedConsumer);
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string>
                {
                    ["MassTransit:RunMigration"] = "true",
                    ["ConnectionStrings:TransportConnection"] = TransportConnection,
                }
            )
            .Build();

        services.AddMessaging(configuration);

        using var provider = services.BuildServiceProvider();

        provider
            .GetRequiredService<IOptions<SqlTransportOptions>>()
            .Value.ConnectionString.Should()
            .Be(TransportConnection, "AddMessaging binds the transport connection string");
        services
            .Should()
            .Contain(d => d.ServiceType == typeof(IBus), "AddMassTransit registered the bus")
            .And.Contain(
                d => d.ImplementationType == typeof(StockCusipChangedConsumer),
                "the [Consumer] assembly scan auto-registered the real consumer"
            );
    }
}
