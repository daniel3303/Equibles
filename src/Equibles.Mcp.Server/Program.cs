using Equibles.Core.AutoWiring;
using Equibles.Data.Extensions;
using Equibles.Fred.Mcp.Extensions;
using Equibles.Holdings.Mcp.Extensions;
using Equibles.InsiderTrading.Mcp.Extensions;
using Equibles.Mcp.Contracts;
using Equibles.Mcp.Extensions;
using Equibles.Mcp.Middleware;
using Equibles.Mcp.Server;
using Equibles.Sec.Mcp.Extensions;
using Equibles.Cftc.Mcp.Extensions;
using Equibles.Cboe.Mcp.Extensions;
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
builder.Services.AddEquiblesDbContext(connectionString, modules => modules.AddAllModules());
builder.Services.AddAllRepositories();

builder.Services.AutoWireServicesFrom<Equibles.Errors.BusinessLogic.ErrorManager>();
builder.Services.AutoWireServicesFrom<Equibles.Sec.BusinessLogic.Search.RagManager>();

builder.Services.AddEquiblesMcp(mcp => {
    mcp.AddHoldings();
    mcp.AddInsiderTrading();
    mcp.AddFred();
    mcp.AddSec();
    mcp.AddCftc();
    mcp.AddCboe();
});

builder.Services.AddSingleton<IApiKeyValidator, SimpleApiKeyValidator>();

var app = builder.Build();

app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments("/mcp"), branch => {
    branch.UseMiddleware<ApiKeyMiddleware>();
});

app.MapMcp("/mcp");

app.Run();
