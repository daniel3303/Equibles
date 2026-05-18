using System.Diagnostics;
using System.Reflection;
using Equibles.Plugins;
using Equibles.Search.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Search.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="SearchAggregator"/> and every <see cref="ISearchProvider"/> found
    /// in loaded <c>Equibles.*</c> assemblies. This mirrors <c>AddAllRepositories</c>: providers are
    /// discovered by interface, so a new module (OSS or commercial) is picked up just by being
    /// loaded — the search service itself never changes (Open/Closed).
    /// </summary>
    public static IServiceCollection AddEquiblesSearch(this IServiceCollection services)
    {
        // Self-load every Equibles.* plugin assembly so discovery never depends on another
        // registrar (e.g. AddAllRepositories) having run first. LoadAll is cached/idempotent.
        PluginLoader.LoadAll();

        var providerTypes = AppDomain
            .CurrentDomain.GetAssemblies()
            .Where(assembly =>
                assembly.FullName != null
                && assembly.FullName.StartsWith("Equibles.", StringComparison.Ordinal)
            )
            .SelectMany(assembly =>
            {
                try
                {
                    return assembly.DefinedTypes;
                }
                catch (ReflectionTypeLoadException exception)
                {
                    // A partially-loadable assembly must not abort discovery of the rest, but the
                    // loader errors are diagnostic — surface them instead of silently swallowing,
                    // and still register whatever types did load.
                    Debug.WriteLine(
                        $"Search provider scan: {assembly.FullName} partially failed to load: "
                            + string.Join(
                                "; ",
                                exception.LoaderExceptions.Select(loaderException =>
                                    loaderException?.Message
                                )
                            )
                    );
                    return exception
                        .Types.Where(type => type != null)
                        .Select(type => type.GetTypeInfo());
                }
            })
            .Where(type =>
                type is { IsClass: true, IsAbstract: false }
                && typeof(ISearchProvider).IsAssignableFrom(type)
            );

        foreach (var providerType in providerTypes)
        {
            var alreadyRegistered = services.Any(descriptor =>
                descriptor.ServiceType == typeof(ISearchProvider)
                && descriptor.ImplementationType == providerType
            );
            if (alreadyRegistered)
            {
                continue;
            }

            services.Add(
                new ServiceDescriptor(typeof(ISearchProvider), providerType, ServiceLifetime.Scoped)
            );
        }

        services.AddScoped<SearchAggregator>();
        return services;
    }
}
