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
using Equibles.FdaCatalysts.Mcp.Extensions;
using Equibles.Finra.Data.Extensions;
using Equibles.Finra.Mcp.Extensions;
using Equibles.Fred.Data.Extensions;
using Equibles.Fred.Mcp.Extensions;
using Equibles.GovernmentContracts.Mcp.Extensions;
using Equibles.Holdings.Data.Extensions;
using Equibles.Holdings.Mcp.Extensions;
using Equibles.InsiderTrading.Data.Extensions;
using Equibles.InsiderTrading.Mcp.Extensions;
using Equibles.Mcp.Contracts;
using Equibles.Mcp.Extensions;
using Equibles.Mcp.Middleware;
using Equibles.Media.Data.Extensions;
using Equibles.Messaging.Extensions;
using Equibles.Sec.Data.Extensions;
using Equibles.Sec.FinancialFacts.Mcp.Extensions;
using Equibles.Sec.Mcp.Extensions;
using Equibles.Yahoo.Data.Extensions;
using Equibles.Yahoo.Mcp.Extensions;
using ModelContextProtocol.AspNetCore;
using Serilog;
using Serilog.Events;

namespace Equibles.Mcp.Server;

public partial class Program
{
    public const string StdioArgument = "--stdio";

    public static async Task Main(string[] args)
    {
        if (args.Contains(StdioArgument))
        {
            await RunStdio(args);
            return;
        }

        var builder = WebApplication.CreateBuilder(args);
        ConfigureServices(builder);
        var app = builder.Build();
        ConfigurePipeline(app);
        await app.RunAsync();
    }

    // Serves MCP over stdin/stdout for clients that launch the server as a subprocess.
    // There is no HTTP pipeline, so the API key and output-format middleware do not apply.
    private static async Task RunStdio(string[] args)
    {
        var settings = new HostApplicationBuilderSettings
        {
            Args = args.Where(arg => arg != StdioArgument).ToArray(),
            Configuration = new ConfigurationManager(),
        };
        // Honour ASPNETCORE_ENVIRONMENT like the HTTP host; DOTNET_ variables and arguments still win.
        settings.Configuration.AddEnvironmentVariables(prefix: "ASPNETCORE_");
        var builder = Host.CreateApplicationBuilder(settings);
        builder.Logging.ClearProviders();
        ConfigureServices(builder, stdio: true);
        await builder.Build().RunAsync();
    }

    public static void ConfigureServices(IHostApplicationBuilder builder, bool stdio = false)
    {
        builder.Services.AddSerilog(config =>
        {
            if (stdio)
            {
                // stdout carries the protocol, so every log event goes to stderr.
                config.MinimumLevel.Warning();
                config.WriteTo.Console(standardErrorFromLevel: LogEventLevel.Verbose);
            }
            else
            {
                config.ReadFrom.Configuration(builder.Configuration);
            }

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
            modules => modules.AddAllModules()
        );
        builder.Services.AddAllRepositories();

        builder.Services.AutoWireServicesFrom<Equibles.Errors.BusinessLogic.ErrorManager>();
        builder.Services.AutoWireServicesFrom<Equibles.Sec.BusinessLogic.Search.RagManager>();
        // Media file access: DocumentTextTools resolves IFileManager to read document
        // content regardless of storage backend.
        builder.Services.AutoWireServicesFrom<Equibles.Media.BusinessLogic.FileManager>();
        // Holdings tools resolve StockCombinedQuarterService to present the in-progress 13F
        // quarter as the combined view.
        builder.Services.AutoWireServicesFrom<Equibles.Holdings.BusinessLogic.StockCombinedQuarterService>();

        // Required for RAG search to embed the query at request time; without this
        // bind EmbeddingConfig is default (Enabled=false) and semantic search is inert.
        builder.Services.Configure<Equibles.Sec.BusinessLogic.Embeddings.EmbeddingConfig>(
            builder.Configuration.GetSection("Embedding")
        );
        // EmbeddingClient resolves IHttpClientFactory unconditionally in its constructor,
        // even when embeddings are disabled -- without this, any Sec search tool
        // (ListFilings, SearchDocuments, SearchDocument) fails to activate.
        builder.Services.AddHttpClient();
        builder.Services.Configure<Equibles.Media.BusinessLogic.Configuration.FileStorageOptions>(
            builder.Configuration.GetSection("FileStorage")
        );
        builder.Services.Configure<Equibles.Core.Configuration.WorkerOptions>(
            builder.Configuration.GetSection("Worker")
        );

        builder.Services.AddEquiblesMcp(
            mcp =>
            {
                mcp.AddHoldings();
                mcp.AddInsiderTrading();
                mcp.AddFred();
                mcp.AddSec();
                mcp.AddFinancialFacts();
                mcp.AddCftc();
                mcp.AddCboe();
                mcp.AddFdaCatalysts();
                mcp.AddCongress();
                mcp.AddShortData();
                mcp.AddStockPrices();
                mcp.AddGovernmentContracts();
            },
            stdio
        );

        builder.Services.AddSingleton<IApiKeyValidator, SimpleApiKeyValidator>();
    }

    public static void ConfigurePipeline(WebApplication app)
    {
        app.UseWhen(
            ctx => ctx.Request.Path.StartsWithSegments("/mcp"),
            branch =>
            {
                branch.UseMiddleware<ApiKeyMiddleware>();
                branch.UseMiddleware<OutputFormatMiddleware>();
            }
        );

        app.MapMcp("/mcp");
    }
}
