using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Media.HostedService.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMediaWorker(this IServiceCollection services)
    {
        services.AddHostedService<FileBackfillWorker>();
        return services;
    }
}
