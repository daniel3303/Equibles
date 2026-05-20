using System.Reflection;
using Equibles.Data;
using Equibles.Messaging.Attributes;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Messaging.Extensions;

public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Wires MassTransit (Postgres SQL transport + EF outbox) and auto-registers
    /// every [Consumer] in matching loaded assemblies. When
    /// <paramref name="consumerAssemblies"/> is null, every non-system assembly
    /// in the current AppDomain is scanned — the default used by the worker host
    /// so a new HostedService's consumer is picked up automatically. Pass a
    /// specific set when the calling host should only own a subset of consumers
    /// (the web host scans just its own assembly so it doesn't try to
    /// instantiate worker-only consumers whose dependencies it never wires).
    /// </summary>
    public static void AddMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        IEnumerable<Assembly> consumerAssemblies = null
    )
    {
        // Only the host that owns the DB should create the transport schema /
        // infrastructure (idempotent, but gated to avoid every host racing it).
        if (configuration.GetValue("MassTransit:RunMigration", false))
        {
            services.AddPostgresMigrationHostedService(x =>
            {
                x.CreateDatabase = false;
                x.CreateInfrastructure = true;
                x.CreateSchema = true;
            });
        }

        services.AddMassTransit(x =>
        {
            // Transactional outbox in the shared EquiblesDbContext: events are
            // captured in the same transaction as the domain write and only
            // delivered after SaveChanges commits.
            x.AddEntityFrameworkOutbox<EquiblesDbContext>(o =>
            {
                o.UsePostgres();
                o.UseBusOutbox();
                o.DuplicateDetectionWindow = TimeSpan.FromMinutes(1);
                o.QueryDelay = TimeSpan.FromSeconds(1);
            });

            var mainAssemblyName =
                Assembly.GetEntryAssembly()?.GetName().Name?.ToLowerInvariant()
                ?? throw new InvalidOperationException("Could not determine main assembly name.");
            x.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter(mainAssemblyName, true));

            // Auto-register every [Consumer] IConsumer<T> across the chosen
            // assemblies. Default scope: every non-system assembly currently
            // loaded (worker behavior); explicit scope: only the assemblies the
            // caller actually owns (web behavior).
            var scannedAssemblies =
                consumerAssemblies
                ?? AppDomain.CurrentDomain.GetAssemblies().Where(a => !IsSystemAssembly(a));

            var consumerTypes = scannedAssemblies
                .SelectMany(a =>
                {
                    try
                    {
                        return a.GetTypes();
                    }
                    catch
                    {
                        return [];
                    }
                })
                .Where(t =>
                    t.GetCustomAttribute<ConsumerAttribute>() != null
                    && t.GetInterfaces()
                        .Any(i =>
                            i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConsumer<>)
                        )
                )
                .ToList();

            foreach (var consumerType in consumerTypes)
            {
                var attribute = consumerType.GetCustomAttribute<ConsumerAttribute>();
                if (attribute == null)
                    continue;

                x.AddConsumer(consumerType)
                    .Endpoint(e =>
                    {
                        e.Name = attribute.AllowMultiple
                            ? $"{mainAssemblyName}-{consumerType.Namespace}-{consumerType.Name}".ToLowerInvariant()
                            : $"{consumerType.Namespace}-{consumerType.Name}".ToLowerInvariant();
                    });
            }

            x.AddSqlMessageScheduler();
            x.UsingPostgres(
                (context, cfg) =>
                {
                    cfg.UseSqlMessageScheduler();
                    cfg.UseJobSagaPartitionKeyFormatters();
                    cfg.AutoStart = true;
                    cfg.UseMessageRetry(r =>
                    {
                        r.Exponential(
                            5,
                            TimeSpan.FromSeconds(5),
                            TimeSpan.FromMinutes(5),
                            TimeSpan.FromSeconds(10)
                        );
                    });

                    cfg.ConfigureEndpoints(context);
                }
            );
        });

        services
            .AddOptions<SqlTransportOptions>()
            .Configure(options =>
            {
                options.ConnectionString = configuration.GetConnectionString("TransportConnection");
            });
    }

    private static bool IsSystemAssembly(Assembly assembly)
    {
        var name = assembly.GetName().Name;
        if (name == null)
            return true;

        return name.StartsWith("System", StringComparison.Ordinal)
            || name.StartsWith("Microsoft", StringComparison.Ordinal)
            || name.StartsWith("netstandard", StringComparison.Ordinal)
            || name.StartsWith("mscorlib", StringComparison.Ordinal);
    }
}
