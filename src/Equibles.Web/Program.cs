using System.IO.Compression;
using System.Net.Sockets;
using Equibles.Core.AutoWiring;
using Equibles.Data;
using Equibles.Data.Extensions;
using Equibles.Messaging.Extensions;
using Equibles.Search.Extensions;
using Equibles.Web.Authentication;
using Equibles.Web.FlashMessage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Serilog;
using Serilog.Events;

namespace Equibles.Web;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        ConfigureServices(builder);
        var app = builder.Build();
        await ApplyMigrationsAsync(app);
        ConfigurePipeline(app);
        await app.RunAsync();
    }

    public static void ConfigureServices(WebApplicationBuilder builder)
    {
        builder.Services.AddSerilog(config =>
        {
            config.ReadFrom.Configuration(builder.Configuration);
            var minLevel = builder.Configuration["MinimumLogLevel"];
            if (
                !string.IsNullOrEmpty(minLevel)
                && Enum.TryParse<LogEventLevel>(minLevel, true, out var level)
            )
            {
                config.MinimumLevel.Is(level);
            }
        });

        Equibles.Plugins.PluginLoader.LoadAll();

        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
        builder.Services.AddEquiblesFinancialDbContext(
            connectionString,
            modules => modules.AddAllModules(),
            migrationsAssembly: typeof(Equibles.Migrations.DesignTimeDbContextFactory).Assembly
        );
        builder.Services.AddAllRepositories();
        // Discovers every ISearchProvider in loaded Equibles.* assemblies (mirrors
        // AddAllRepositories) so new modules join global search with no host change.
        builder.Services.AddEquiblesSearch();

        // MassTransit (Postgres SQL transport, no outbox in OSS — direct publish).
        // Web subscribes to events published by other hosts — e.g. the live
        // ScraperActivity feed from the worker. The consumer scan is restricted
        // to Equibles.Web's own assembly: worker-only consumers (e.g. the
        // Holdings rescan signal handler) require services the web host never
        // registers, so picking them up here would crash service-provider
        // validation as soon as a referenced HostedService assembly loads.
        builder.Services.AddMessaging(
            builder.Configuration,
            consumerAssemblies: [typeof(Program).Assembly]
        );

        builder.Services.AutoWireServicesFrom<Equibles.Errors.BusinessLogic.ErrorManager>();
        builder.Services.AutoWireServicesFrom<Equibles.Web.Services.StockTabService>();

        var authSettings =
            builder.Configuration.GetSection("Auth").Get<AuthSettings>() ?? new AuthSettings();
        builder.Services.Configure<AuthSettings>(builder.Configuration.GetSection("Auth"));

        builder
            .Services.AddAuthentication(EnvAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, EnvAuthHandler>(
                EnvAuthHandler.SchemeName,
                null
            );

        if (authSettings.IsEnabled)
        {
            builder.Services.AddAuthorization(options =>
            {
                options.FallbackPolicy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build();
            });
        }
        else
        {
            builder.Services.AddAuthorization();
        }

        builder.Services.Configure<RouteOptions>(options =>
        {
            options.LowercaseUrls = true;
        });

        builder.Services.AddHttpClient();

        builder.Services.AddScoped<Equibles.Web.Filters.StatusBadgeFilter>();
        builder.Services.AddScoped<Equibles.Web.Filters.VersionCheckFilter>();
        builder
            .Services.AddControllersWithViews(options =>
            {
                options.Filters.AddService<Equibles.Web.Filters.StatusBadgeFilter>();
                options.Filters.AddService<Equibles.Web.Filters.VersionCheckFilter>();
            })
            .AddRazorRuntimeCompilation();

        var keysDirectory = builder.Configuration["DataProtection:KeysDirectory"] ?? "/app/keys";
        builder
            .Services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keysDirectory));

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSession();
        builder.Services.AddFlashMessage();
        builder.Services.AddHealthChecks();

        builder.Services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
        });
        builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
        {
            options.Level = CompressionLevel.Fastest;
        });
        builder.Services.Configure<GzipCompressionProviderOptions>(options =>
        {
            options.Level = CompressionLevel.Fastest;
        });
    }

    public static async Task ApplyMigrationsAsync(WebApplication app)
    {
        // Extended timeout for index rebuilds.
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        dbContext.Database.SetCommandTimeout(TimeSpan.FromHours(1));

        // ParadeDB's init script briefly accepts connections on the Unix
        // socket while the TCP listener is still down, which can release us
        // a few seconds before Postgres is reachable. Retry transient
        // connection failures; non-connection errors fail fast.
        await RetryOnTransientConnectionFailure(
            () => dbContext.Database.MigrateAsync(),
            logger,
            maxAttempts: 30,
            delay: TimeSpan.FromSeconds(2)
        );
    }

    public static async Task RetryOnTransientConnectionFailure(
        Func<Task> operation,
        Microsoft.Extensions.Logging.ILogger logger,
        int maxAttempts,
        TimeSpan delay
    )
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await operation();
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts && IsTransientConnectionFailure(ex))
            {
                logger.LogWarning(
                    ex,
                    "Database not reachable yet (attempt {Attempt}/{MaxAttempts}); retrying in {Delay}s.",
                    attempt,
                    maxAttempts,
                    delay.TotalSeconds
                );
                await Task.Delay(delay);
            }
        }
    }

    public static bool IsTransientConnectionFailure(Exception ex) =>
        ex switch
        {
            // TCP refused / unreachable while ParadeDB is restarting.
            NpgsqlException { InnerException: SocketException } => true,
            // "cannot_connect_now" — server is in startup.
            PostgresException { SqlState: "57P03" } => true,
            _ => false,
        };

    public static void ConfigurePipeline(WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Home/Error");
        }

        app.UseResponseCompression();

        app.UseStaticFiles(
            new StaticFileOptions
            {
                OnPrepareResponse = ctx =>
                {
                    var headers = ctx.Context.Response.Headers;

                    if (ctx.Context.Request.Path.StartsWithSegments("/dist"))
                    {
                        // Vite-built assets use asp-append-version (content hash query string),
                        // so they can be cached indefinitely — a new hash busts the cache on deploy.
                        headers.CacheControl = "public, max-age=31536000, immutable";
                    }
                    else
                    {
                        headers.CacheControl = "public, max-age=86400";
                    }
                },
            }
        );
        app.UseRouting();
        app.UseSession();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapHealthChecks("/healthz").AllowAnonymous();
        app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");
    }
}
