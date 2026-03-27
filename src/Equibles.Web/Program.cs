using Equibles.CommonStocks.Data.Extensions;
using Equibles.Congress.Data.Extensions;
using Equibles.Core.AutoWiring;
using Equibles.Data;
using Equibles.Data.Extensions;
using Equibles.Errors.Data.Extensions;
using Equibles.Holdings.Data.Extensions;
using Equibles.InsiderTrading.Data.Extensions;
using Equibles.Media.Data.Extensions;
using Equibles.Sec.Data.Extensions;
using Equibles.Fred.Data.Extensions;
using Equibles.ShortData.Data.Extensions;
using Equibles.Web.Authentication;
using Equibles.Web.FlashMessage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog(config => {
    config.ReadFrom.Configuration(builder.Configuration);
    var minLevel = builder.Configuration["MinimumLogLevel"];
    if (!string.IsNullOrEmpty(minLevel) && Enum.TryParse<LogEventLevel>(minLevel, true, out var level)) {
        config.MinimumLevel.Is(level);
    }
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddEquiblesDbContext(connectionString, modules => {
    modules.AddCommonStocks();
    modules.AddHoldings();
    modules.AddInsiderTrading();
    modules.AddCongress();
    modules.AddShortData();
    modules.AddFred();
    modules.AddSec();
    modules.AddMedia();
    modules.AddErrors();
}, migrationsAssembly: typeof(Equibles.Migrations.DesignTimeDbContextFactory).Assembly);

builder.Services.AddRepositoriesFrom(
    typeof(Equibles.CommonStocks.Repositories.CommonStockRepository).Assembly,
    typeof(Equibles.Holdings.Repositories.InstitutionalHolderRepository).Assembly,
    typeof(Equibles.InsiderTrading.Repositories.InsiderOwnerRepository).Assembly,
    typeof(Equibles.Congress.Repositories.CongressMemberRepository).Assembly,
    typeof(Equibles.ShortData.Repositories.DailyShortVolumeRepository).Assembly,
    typeof(Equibles.Fred.Repositories.FredSeriesRepository).Assembly,
    typeof(Equibles.Sec.Repositories.DocumentRepository).Assembly,
    typeof(Equibles.Media.Repositories.FileRepository).Assembly,
    typeof(Equibles.Errors.Repositories.ErrorRepository).Assembly
);

builder.Services.AutoWireServicesFrom<Equibles.Errors.BusinessLogic.ErrorManager>();

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

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo("/app/keys"));

builder.Services.AddHttpContextAccessor();
builder.Services.AddSession();
builder.Services.AddFlashMessage();
builder.Services.AddHealthChecks();

var app = builder.Build();

// Apply pending EF Core migrations on startup (extended timeout for index rebuilds)
using (var scope = app.Services.CreateScope()) {
    var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesDbContext>();
    dbContext.Database.SetCommandTimeout(TimeSpan.FromHours(1));
    await dbContext.Database.MigrateAsync();
}

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

app.Run();
