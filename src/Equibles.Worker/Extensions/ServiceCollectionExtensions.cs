using Equibles.Core.AutoWiring;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Worker.Extensions;

public static class ServiceCollectionExtensions {
    public static IServiceCollection AddWorkerServices(this IServiceCollection services) {
        services.AutoWireServicesFrom<TickerMapService>();
        return services;
    }
}
