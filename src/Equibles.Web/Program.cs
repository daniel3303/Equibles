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
using Equibles.ShortData.Data.Extensions;
using Equibles.Web.FlashMessage;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog(config => config
    .ReadFrom.Configuration(builder.Configuration));

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddEquiblesDbContext(connectionString, modules => {
    modules.AddCommonStocks();
    modules.AddHoldings();
    modules.AddInsiderTrading();
    modules.AddCongress();
    modules.AddShortData();
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
    typeof(Equibles.Sec.Repositories.DocumentRepository).Assembly,
    typeof(Equibles.Media.Repositories.FileRepository).Assembly,
    typeof(Equibles.Errors.Repositories.ErrorRepository).Assembly
);

builder.Services.AutoWireServicesFrom<Equibles.Errors.BusinessLogic.ErrorManager>();

builder.Services.AddControllersWithViews()
    .AddRazorRuntimeCompilation();

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

app.MapHealthChecks("/healthz");
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
