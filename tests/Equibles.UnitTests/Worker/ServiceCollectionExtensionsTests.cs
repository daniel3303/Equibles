using Equibles.Core.Configuration;
using Equibles.Data.Extensions;
using Equibles.Worker;
using Equibles.Worker.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Equibles.UnitTests.Worker;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddWorkerServices_RegistersTickerMapServiceViaAutoWiring()
    {
        // AddWorkerServices is the host's seam into auto-wiring — it scans
        // the Equibles.Worker assembly and registers every [Service]-
        // attributed type. The composition root (Equibles.Worker.Host)
        // depends on this side-effect: if the scan stops finding services
        // the host boots without ScraperWorker dependencies and silently
        // does no work. TickerMapService carries [Service] and is the
        // canonical worker-assembly type; pin it as the smoke test so a
        // refactor that swaps AutoWireServicesFrom for a different scanner
        // (or accidentally points at the wrong assembly marker) surfaces
        // here.
        var services = new ServiceCollection();

        services.AddWorkerServices();

        services.Should().Contain(d => d.ServiceType == typeof(TickerMapService));
    }

    [Fact]
    public async Task AddWorkerServices_ConnectionString_IsolatesAndBoundsTheLeasePool()
    {
        const string queryConnectionString =
            "Host=localhost;Database=equibles;Username=postgres;Password=postgres;"
            + "Pooling=false;Minimum Pool Size=24;Maximum Pool Size=24;Multiplexing=true;"
            + "No Reset On Close=true";
        var workerOptions = new WorkerOptions();
        var services = new ServiceCollection();
        services.AddSingleton<IOptions<WorkerOptions>>(Options.Create(workerOptions));

        services.AddWorkerServices(queryConnectionString);

        await using var provider = services.BuildServiceProvider();
        var leaseDataSource = provider.GetRequiredService<ScraperLeaseDataSource>();
        var leaseSettings = new NpgsqlConnectionStringBuilder(leaseDataSource.ConnectionString);
        var querySettings = new NpgsqlConnectionStringBuilder(queryConnectionString);

        workerOptions.LaneLeasePoolSize.Should().Be(8);
        leaseDataSource.MaximumPoolSize.Should().Be(workerOptions.LaneLeasePoolSize);
        leaseSettings.Pooling.Should().BeTrue();
        leaseSettings.MinPoolSize.Should().Be(0);
        leaseSettings.MaxPoolSize.Should().Be(workerOptions.LaneLeasePoolSize);
        leaseSettings.Multiplexing.Should().BeFalse();
        leaseSettings.NoResetOnClose.Should().BeFalse();
        querySettings.Pooling.Should().BeFalse();
        querySettings.MinPoolSize.Should().Be(24);
        querySettings.MaxPoolSize.Should().Be(24);
        querySettings.Multiplexing.Should().BeTrue();
        querySettings.NoResetOnClose.Should().BeTrue();
        provider
            .GetRequiredService<ScraperLeaseDataSource>()
            .Should()
            .BeSameAs(leaseDataSource, "one process must share one dedicated lease pool");
    }

    [Fact]
    public async Task AddWorkerServices_WithoutConnectionString_UsesTheFinancialDbContextSettings()
    {
        const string queryConnectionString =
            "Host=database.internal;Database=equibles;Username=postgres;Password=postgres;"
            + "Maximum Pool Size=24";
        var services = new ServiceCollection();
        services.AddSingleton<IOptions<WorkerOptions>>(Options.Create(new WorkerOptions()));
        services.AddEquiblesFinancialDbContext(queryConnectionString, _ => { });

        services.AddWorkerServices();

        await using var provider = services.BuildServiceProvider();
        var leaseDataSource = provider.GetRequiredService<ScraperLeaseDataSource>();
        var leaseSettings = new NpgsqlConnectionStringBuilder(leaseDataSource.ConnectionString);

        leaseSettings.Host.Should().Be("database.internal");
        leaseSettings.Database.Should().Be("equibles");
        leaseSettings.MaxPoolSize.Should().Be(new WorkerOptions().LaneLeasePoolSize);
    }
}
