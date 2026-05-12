using Equibles.Core.AutoWiring;
using Equibles.Data;
using Equibles.Data.Extensions;
using Equibles.Web.Authentication;
using Equibles.Web.FlashMessage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;

namespace Equibles.Web;

public partial class Program {
    public static async Task Main(string[] args) {
        var builder = WebApplication.CreateBuilder(args);
        ConfigureServices(builder);
        var app = builder.Build();
        await ApplyMigrationsAsync(app);
        ConfigurePipeline(app);
        await app.RunAsync();
    }

    public static void ConfigureServices(WebApplicationBuilder builder) {
        builder.Services.AddSerilog(config => {
            config.ReadFrom.Configuration(builder.Configuration);
            var minLevel = builder.Configuration["MinimumLogLevel"];
            if (!string.IsNullOrEmpty(minLevel) && Enum.TryParse<LogEventLevel>(minLevel, true, out var level)) {
                config.MinimumLevel.Is(level);
            }
        });

        Equibles.Plugins.PluginLoader.LoadAll();

        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
        builder.Services.AddEquiblesDbContext(connectionString,
            modules => modules.AddAllModules(),
            migrationsAssembly: typeof(Equibles.Migrations.DesignTimeDbContextFactory).Assembly);
        builder.Services.AddAllRepositories();

        builder.Services.AutoWireServicesFrom<Equibles.Errors.BusinessLogic.ErrorManager>();
        builder.Services.AutoWireServicesFrom<Equibles.Web.Services.StockTabService>();

        var authSettings = builder.Configuration.GetSection("Auth").Get<AuthSettings>() ?? new AuthSettings();
        builder.Services.Configure<AuthSettings>(builder.Configuration.GetSection("Auth"));

        builder.Services.AddAuthentication(EnvAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, EnvAuthHandler>(EnvAuthHandler.SchemeName, null);

        if (authSettings.IsEnabled) {
            builder.Services.AddAuthorization(options => {
                options.FallbackPolicy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build();
            });
        } else {
            builder.Services.AddAuthorization();
        }

        builder.Services.Configure<RouteOptions>(options => {
            options.LowercaseUrls = true;
        });

        builder.Services.AddScoped<Equibles.Web.Filters.StatusBadgeFilter>();
        builder.Services.AddControllersWithViews(options => {
                options.Filters.AddService<Equibles.Web.Filters.StatusBadgeFilter>();
            })
            .AddRazorRuntimeCompilation();

        var keysDirectory = builder.Configuration["DataProtection:KeysDirectory"] ?? "/app/keys";
        builder.Services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keysDirectory));

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSession();
        builder.Services.AddFlashMessage();
        builder.Services.AddHealthChecks();
    }

    public static async Task ApplyMigrationsAsync(WebApplication app) {
        // Extended timeout for index rebuilds.
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesDbContext>();
        dbContext.Database.SetCommandTimeout(TimeSpan.FromHours(1));
        await dbContext.Database.MigrateAsync();
    }

    public static void ConfigurePipeline(WebApplication app) {
        if (!app.Environment.IsDevelopment()) {
            app.UseExceptionHandler("/Home/Error");
        }

        app.UseStaticFiles();
        app.UseRouting();
        app.UseSession();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapHealthChecks("/healthz").AllowAnonymous();
        app.MapControllerRoute(
            name: "default",
            pattern: "{controller=Home}/{action=Index}/{id?}");
    }
}
