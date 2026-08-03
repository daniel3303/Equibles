using Equibles.Core.AutoWiring;
using Equibles.Core.Configuration;
using Equibles.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Equibles.Worker.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWorkerServices(this IServiceCollection services)
    {
        return AddWorkerServices(services, ResolveFinancialConnectionString);
    }

    public static IServiceCollection AddWorkerServices(
        this IServiceCollection services,
        string connectionString
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return AddWorkerServices(services, _ => connectionString);
    }

    private static IServiceCollection AddWorkerServices(
        IServiceCollection services,
        Func<IServiceProvider, string> connectionStringFactory
    )
    {
        services.AutoWireServicesFrom<TickerMapService>();
        services.AddSingleton(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<WorkerOptions>>().Value;
            return new ScraperLeaseDataSource(
                connectionStringFactory(serviceProvider),
                options.LaneLeasePoolSize
            );
        });
        return services;
    }

    private static string ResolveFinancialConnectionString(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        return dbContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException(
                "EquiblesFinancialDbContext has no connection string. Call AddWorkerServices with "
                    + "the worker database connection string."
            );
    }
}
