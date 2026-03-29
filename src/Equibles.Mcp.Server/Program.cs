using Equibles.CommonStocks.Data.Extensions;
using Equibles.Core.AutoWiring;
using Equibles.Data.Extensions;
using Equibles.Errors.Data.Extensions;
using Equibles.Fred.Data.Extensions;
using Equibles.Fred.Mcp.Extensions;
using Equibles.Holdings.Data.Extensions;
using Equibles.Holdings.Mcp.Extensions;
using Equibles.InsiderTrading.Data.Extensions;
using Equibles.InsiderTrading.Mcp.Extensions;
using Equibles.Media.Data.Extensions;
using Equibles.Mcp.Contracts;
using Equibles.Mcp.Extensions;
using Equibles.Mcp.Middleware;
using Equibles.Mcp.Server;
using Equibles.Congress.Data.Extensions;
using Equibles.Finra.Data.Extensions;
using Equibles.Sec.Data.Extensions;
using Equibles.Yahoo.Data.Extensions;
using Equibles.Sec.Mcp.Extensions;
using ModelContextProtocol.AspNetCore;
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
    modules.AddFred();
    modules.AddSec();
    modules.AddCongress();
    modules.AddFinra();
    modules.AddYahoo();
    modules.AddMedia();
    modules.AddErrors();
});

builder.Services.AddRepositoriesFrom(
    typeof(Equibles.CommonStocks.Repositories.CommonStockRepository).Assembly,
    typeof(Equibles.Holdings.Repositories.InstitutionalHolderRepository).Assembly,
    typeof(Equibles.InsiderTrading.Repositories.InsiderOwnerRepository).Assembly,
    typeof(Equibles.Fred.Repositories.FredSeriesRepository).Assembly,
    typeof(Equibles.Sec.Repositories.DocumentRepository).Assembly,
    typeof(Equibles.Congress.Repositories.CongressMemberRepository).Assembly,
    typeof(Equibles.Finra.Repositories.DailyShortVolumeRepository).Assembly,
    typeof(Equibles.Yahoo.Repositories.DailyStockPriceRepository).Assembly,
    typeof(Equibles.Media.Repositories.FileRepository).Assembly,
    typeof(Equibles.Errors.Repositories.ErrorRepository).Assembly
);

builder.Services.AutoWireServicesFrom<Equibles.Errors.BusinessLogic.ErrorManager>();
builder.Services.AutoWireServicesFrom<Equibles.Sec.BusinessLogic.Search.RagManager>();

builder.Services.AddEquiblesMcp(mcp => {
    mcp.AddHoldings();
    mcp.AddInsiderTrading();
    mcp.AddFred();
    mcp.AddSec();
});

builder.Services.AddSingleton<IApiKeyValidator, SimpleApiKeyValidator>();

var app = builder.Build();

app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments("/mcp"), branch => {
    branch.UseMiddleware<ApiKeyMiddleware>();
});

app.MapMcp("/mcp");

app.Run();
