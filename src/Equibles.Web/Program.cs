using System.IO.Compression;
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
        builder.Services.AddEquiblesDbContext(
            connectionString,
            // .AddMessaging() explicitly: the MassTransit outbox entities are in
            // the shared migration snapshot, so every host that runs/validates
            // migrations must include them or EF throws PendingModelChanges.
            // AddAllModules' reflection only sees already-loaded assemblies, so
            // the explicit call guarantees it deterministically.
            modules => modules.AddAllModules().AddMessaging(),
            migrationsAssembly: typeof(Equibles.Migrations.DesignTimeDbContextFactory).Assembly
        );
        builder.Services.AddAllRepositories();
        // Discovers every ISearchProvider in loaded Equibles.* assemblies (mirrors
        // AddAllRepositories) so new modules join global search with no host change.
        builder.Services.AddEquiblesSearch();

        // MassTransit (Postgres SQL transport + EF outbox in EquiblesDbContext).
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
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesDbContext>();
        dbContext.Database.SetCommandTimeout(TimeSpan.FromHours(1));
        await dbContext.Database.MigrateAsync();
    }

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
