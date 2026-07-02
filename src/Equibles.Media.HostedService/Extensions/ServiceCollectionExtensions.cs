using Equibles.Media.BusinessLogic.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Equibles.Media.HostedService.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMediaWorker(this IServiceCollection services)
    {
        // TryAddEnumerable so a host registering additional checkers (e.g. a second
        // database context) composes with this default instead of duplicating it.
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IBlobReferenceChecker, FinancialBlobReferenceChecker>()
        );

        services.AddHostedService<FileBackfillWorker>();
        services.AddHostedService<BlobDeletionSweepWorker>();
        return services;
    }
}
