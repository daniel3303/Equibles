using System.Reflection;
using Equibles.ParadeDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Data.Extensions;

public static class ServiceCollectionExtensions {
    public static IServiceCollection AddEquiblesDbContext(
        this IServiceCollection services,
        string connectionString,
        Action<EquiblesModuleBuilder> configureModules,
        Assembly migrationsAssembly = null,
        TimeSpan? commandTimeout = null) {
        var moduleBuilder = new EquiblesModuleBuilder();
        configureModules(moduleBuilder);

        foreach (var module in moduleBuilder.Modules) {
            services.AddSingleton<IModuleConfiguration>(module);
        }

        services.AddDbContext<EquiblesDbContext>((sp, options) => {
            options.UseNpgsql(connectionString, npgsql => {
                npgsql.UseVector()
                    .UseParadeDb()
                    .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                if (migrationsAssembly != null) {
                    npgsql.MigrationsAssembly(migrationsAssembly);
                }
                if (commandTimeout.HasValue) {
                    npgsql.CommandTimeout((int)commandTimeout.Value.TotalSeconds);
                }
            });
            options.UseLazyLoadingProxies();
        });

        return services;
    }

    public static IServiceCollection AddRepositoriesFrom(this IServiceCollection services, params Assembly[] assemblies) {
        foreach (var assembly in assemblies) {
            var repositories = assembly.DefinedTypes.Where(t =>
                t is { IsClass: true, IsAbstract: false, IsInterface: false } &&
                IsSubClassOfGenericType(t, typeof(BaseRepository<>)));

            foreach (var repository in repositories) {
                services.Add(new ServiceDescriptor(repository, repository, ServiceLifetime.Scoped));
            }
        }

        return services;
    }

    private static bool IsSubClassOfGenericType(Type type, Type genericType) {
        var currentType = type;
        while (currentType != null) {
            if (currentType.IsGenericType && currentType.GetGenericTypeDefinition() == genericType) {
                return true;
            }

            currentType = currentType.BaseType;
        }

        return false;
    }
}
