using System.Reflection;
using Equibles.ParadeDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Equibles.Data.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers an Equibles DB context of type <typeparamref name="TContext"/>
    /// with its own module set. The module list is captured per context (via
    /// <see cref="ModuleConfigurationSet{TContext}"/>) so multiple contexts in the
    /// same host never share a configuration set. Postgres extensions are NOT
    /// applied here — pass <paramref name="configureNpgsql"/> to opt a context
    /// into pgvector / ParadeDB (see <see cref="AddEquiblesFinancialDbContext"/>).
    /// </summary>
    public static IServiceCollection AddEquiblesDbContext<TContext>(
        this IServiceCollection services,
        string connectionString,
        Action<EquiblesModuleBuilder> configureModules,
        Action<NpgsqlDbContextOptionsBuilder> configureNpgsql = null,
        Assembly migrationsAssembly = null,
        string migrationsAssemblyName = null,
        TimeSpan? commandTimeout = null,
        Action<DbContextOptionsBuilder> configureOptions = null
    )
        where TContext : EquiblesDbContextBase
    {
        var moduleBuilder = new EquiblesModuleBuilder();
        configureModules(moduleBuilder);

        services.AddSingleton(new ModuleConfigurationSet<TContext>(moduleBuilder.Modules));

        services.AddDbContext<TContext>(
            (sp, options) =>
            {
                options.UseNpgsql(
                    connectionString,
                    npgsql =>
                    {
                        npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                        configureNpgsql?.Invoke(npgsql);
                        if (migrationsAssembly != null)
                        {
                            npgsql.MigrationsAssembly(migrationsAssembly);
                        }
                        else if (migrationsAssemblyName != null)
                        {
                            npgsql.MigrationsAssembly(migrationsAssemblyName);
                        }
                        if (commandTimeout.HasValue)
                        {
                            npgsql.CommandTimeout((int)commandTimeout.Value.TotalSeconds);
                        }
                    }
                );
                options.UseLazyLoadingProxies();
                // Last, so a host can adjust context-level options (e.g. suppress a
                // warning) on top of the standard configuration.
                configureOptions?.Invoke(options);
            }
        );

        return services;
    }

    /// <summary>
    /// Registers the <see cref="EquiblesFinancialDbContext"/> with pgvector +
    /// ParadeDB enabled. Convenience wrapper over
    /// <see cref="AddEquiblesDbContext{TContext}"/> for the public financial database.
    /// </summary>
    public static IServiceCollection AddEquiblesFinancialDbContext(
        this IServiceCollection services,
        string connectionString,
        Action<EquiblesModuleBuilder> configureModules,
        Assembly migrationsAssembly = null,
        string migrationsAssemblyName = null,
        TimeSpan? commandTimeout = null,
        Action<DbContextOptionsBuilder> configureOptions = null
    )
    {
        return services.AddEquiblesDbContext<EquiblesFinancialDbContext>(
            connectionString,
            configureModules,
            npgsql => npgsql.UseVector().UseParadeDb(),
            migrationsAssembly,
            migrationsAssemblyName,
            commandTimeout,
            configureOptions
        );
    }

    public static IServiceCollection AddAllRepositories(this IServiceCollection services)
    {
        var assemblies = AppDomain
            .CurrentDomain.GetAssemblies()
            .Where(a =>
                a.FullName != null && a.FullName.StartsWith("Equibles.", StringComparison.Ordinal)
            )
            .ToArray();
        return services.AddRepositoriesFrom(assemblies);
    }

    public static IServiceCollection AddRepositoriesFrom(
        this IServiceCollection services,
        params Assembly[] assemblies
    )
    {
        foreach (var assembly in assemblies)
        {
            var repositories = assembly.DefinedTypes.Where(t =>
                t is { IsClass: true, IsAbstract: false, IsInterface: false }
                && (
                    IsSubClassOfGenericType(t, typeof(BaseRepository<>))
                    || IsSubClassOfGenericType(t, typeof(BaseRepository<,>))
                )
            );

            foreach (var repository in repositories)
            {
                services.Add(new ServiceDescriptor(repository, repository, ServiceLifetime.Scoped));
            }
        }

        return services;
    }

    private static bool IsSubClassOfGenericType(Type type, Type genericType)
    {
        var currentType = type;
        while (currentType != null)
        {
            if (currentType.IsGenericType && currentType.GetGenericTypeDefinition() == genericType)
            {
                return true;
            }

            currentType = currentType.BaseType;
        }

        return false;
    }
}
