using Equibles.Cboe.Data.Extensions;
using Equibles.Cboe.Mcp.Extensions;
using Equibles.Cftc.Data.Extensions;
using Equibles.Cftc.Mcp.Extensions;
using Equibles.CommonStocks.Data.Extensions;
using Equibles.Congress.Data.Extensions;
using Equibles.Congress.Mcp.Extensions;
using Equibles.Core.AutoWiring;
using Equibles.Data;
using Equibles.Data.Extensions;
using Equibles.Errors.Data.Extensions;
using Equibles.Finra.Data.Extensions;
using Equibles.Finra.Mcp.Extensions;
using Equibles.Fred.Data.Extensions;
using Equibles.Fred.Mcp.Extensions;
using Equibles.Holdings.Data.Extensions;
using Equibles.Holdings.Mcp.Extensions;
using Equibles.InsiderTrading.Data.Extensions;
using Equibles.InsiderTrading.Mcp.Extensions;
using Equibles.Mcp.Contracts;
using Equibles.Mcp.Extensions;
using Equibles.Mcp.Middleware;
using Equibles.Media.Data.Extensions;
using Equibles.Sec.Data.Extensions;
using Equibles.Sec.Mcp.Extensions;
using Equibles.Yahoo.Data.Extensions;
using Equibles.Yahoo.Mcp.Extensions;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.AspNetCore;
using Serilog;
using Serilog.Events;

namespace Equibles.Mcp.Server;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        ConfigureServices(builder);
        var app = builder.Build();
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
        builder.Services.AddEquiblesDbContext(connectionString, modules => modules.AddAllModules());
        builder.Services.AddAllRepositories();

        builder.Services.AutoWireServicesFrom<Equibles.Errors.BusinessLogic.ErrorManager>();
        builder.Services.AutoWireServicesFrom<Equibles.Sec.BusinessLogic.Search.RagManager>();

        // Required for RAG search to embed the query at request time; without this
        // bind EmbeddingConfig is default (Enabled=false) and semantic search is inert.
        builder.Services.Configure<Equibles.Sec.BusinessLogic.Embeddings.EmbeddingConfig>(
            builder.Configuration.GetSection("Embedding")
        );

        builder.Services.AddEquiblesMcp(mcp =>
        {
            mcp.AddHoldings();
            mcp.AddInsiderTrading();
            mcp.AddFred();
            mcp.AddSec();
            mcp.AddCftc();
            mcp.AddCboe();
            mcp.AddCongress();
            mcp.AddShortData();
            mcp.AddStockPrices();
        });

        builder.Services.AddSingleton<IApiKeyValidator, SimpleApiKeyValidator>();
    }

    public static async Task ApplyMigrationsAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesDbContext>();
        dbContext.Database.SetCommandTimeout(TimeSpan.FromHours(1));
        await dbContext.Database.MigrateAsync();
    }

    public static void ConfigurePipeline(WebApplication app)
    {
        app.UseWhen(
            ctx => ctx.Request.Path.StartsWithSegments("/mcp"),
            branch =>
            {
                branch.UseMiddleware<ApiKeyMiddleware>();
            }
        );

        app.MapMcp("/mcp");
    }
}
