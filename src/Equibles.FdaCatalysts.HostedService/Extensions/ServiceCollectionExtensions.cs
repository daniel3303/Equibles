using Equibles.Core.AutoWiring;
using Equibles.FdaCatalysts.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.FdaCatalysts.HostedService.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFdaCatalystWorker(this IServiceCollection services)
    {
        services.AutoWireServicesFrom<FdaAdvisoryCommitteeCalendarImportService>();
        services.AddHostedService<FdaCatalystScraperWorker>();
        return services;
    }
}
