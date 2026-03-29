using Equibles.Cboe.Data.Extensions;
using Equibles.Cboe.HostedService.Extensions;
using Equibles.Cftc.Data.Extensions;
using Equibles.Cftc.HostedService.Extensions;
using Equibles.CommonStocks.Data.Extensions;
using Equibles.Congress.Data.Extensions;
using Equibles.Congress.HostedService.Extensions;
using Equibles.Core.AutoWiring;
using Equibles.Core.Configuration;
using Equibles.Data.Extensions;
using Equibles.Errors.Data.Extensions;
using Equibles.Finra.Data.Extensions;
using Equibles.Finra.HostedService.Extensions;
using Equibles.Fred.Data.Extensions;
using Equibles.Fred.HostedService.Extensions;
using Equibles.Holdings.Data.Extensions;
using Equibles.Holdings.HostedService.Extensions;
using Equibles.InsiderTrading.Data.Extensions;
using Equibles.Media.Data.Extensions;
using Equibles.Sec.Data.Extensions;
using Equibles.Sec.HostedService.Configuration;
using Equibles.Sec.HostedService.Extensions;
using Equibles.Worker.Extensions;
using Equibles.Yahoo.Data.Extensions;
using Equibles.Yahoo.HostedService.Extensions;
using Serilog;
using Serilog.Events;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog(config => {
    config.ReadFrom.Configuration(builder.Configuration);
    var minLevel = builder.Configuration["MinimumLogLevel"];
    if (!string.IsNullOrEmpty(minLevel) && Enum.TryParse<LogEventLevel>(minLevel, true, out var level)) {
        config.MinimumLevel.Is(level);
    }
});

Equibles.Plugins.PluginLoader.LoadAll();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddEquiblesDbContext(connectionString, modules => modules.AddAllModules());
builder.Services.AddAllRepositories();

// Worker-specific configuration
builder.Services.Configure<WorkerOptions>(
    builder.Configuration.GetSection("Worker"));
builder.Services.Configure<DocumentScraperOptions>(
    builder.Configuration.GetSection("DocumentScraper"));
builder.Services.Configure<Equibles.Integrations.Finra.Configuration.FinraOptions>(
    builder.Configuration.GetSection("Finra"));
builder.Services.Configure<Equibles.Finra.HostedService.Configuration.FinraScraperOptions>(
    builder.Configuration.GetSection("FinraScraper"));
builder.Services.Configure<Equibles.Sec.HostedService.Configuration.FtdScraperOptions>(
    builder.Configuration.GetSection("FtdScraper"));
builder.Services.Configure<Equibles.Fred.HostedService.Configuration.FredScraperOptions>(
    builder.Configuration.GetSection("FredScraper"));
builder.Services.Configure<Equibles.Integrations.Fred.Configuration.FredOptions>(
    builder.Configuration.GetSection("Fred"));
builder.Services.Configure<Equibles.Yahoo.HostedService.Configuration.YahooPriceScraperOptions>(
    builder.Configuration.GetSection("YahooPriceScraper"));
builder.Services.Configure<Equibles.Cftc.HostedService.Configuration.CftcScraperOptions>(
    builder.Configuration.GetSection("CftcScraper"));
builder.Services.Configure<Equibles.Cboe.HostedService.Configuration.CboeScraperOptions>(
    builder.Configuration.GetSection("CboeScraper"));

builder.Services.AddHttpClient();

// AutoWire OSS business logic services
builder.Services.AutoWireServicesFrom<Equibles.Errors.BusinessLogic.ErrorManager>();
builder.Services.AutoWireServicesFrom<Equibles.CommonStocks.BusinessLogic.CommonStockManager>();
builder.Services.AutoWireServicesFrom<Equibles.Media.BusinessLogic.FileManager>();
builder.Services.AutoWireServicesFrom<Equibles.Sec.BusinessLogic.SecDocumentHtmlNormalizer>();

// Register worker services and all scrapers
builder.Services.AddWorkerServices();
builder.Services.AddSecWorker();
builder.Services.AddFinraWorker();
builder.Services.AddFredWorker();
builder.Services.AddYahooWorker();
builder.Services.AddCftcWorker();
builder.Services.AddCboeWorker();
builder.Services.AddCongressWorker();
builder.Services.AddHoldingsWorker();

// Cross-module services (interface-based, need manual registration)
builder.Services.AddScoped<Equibles.Core.Contracts.IStockPriceProvider, Equibles.Yahoo.Repositories.YahooStockPriceProvider>();

var host = builder.Build();
host.Run();
