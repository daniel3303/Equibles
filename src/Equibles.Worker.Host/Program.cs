using Equibles.Core.AutoWiring;
using Equibles.Core.Configuration;
using Equibles.Data.Extensions;
using Equibles.Sec.HostedService;
using Equibles.Sec.HostedService.Configuration;
using Equibles.Sec.HostedService.Contracts;
using Equibles.Sec.HostedService.Services;
using Equibles.Fred.HostedService;
using Equibles.Finra.HostedService;
using Equibles.Yahoo.HostedService;
using Equibles.Cftc.HostedService;
using Equibles.Cboe.HostedService;
using Equibles.Congress.HostedService;
using Equibles.Holdings.HostedService;
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

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddEquiblesDbContext(connectionString, modules => modules.AddAllModules());
builder.Services.AddAllRepositories();

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

builder.Services.AutoWireServicesFrom<Equibles.Errors.BusinessLogic.ErrorManager>();
builder.Services.AutoWireServicesFrom<Equibles.CommonStocks.BusinessLogic.CommonStockManager>();
builder.Services.AutoWireServicesFrom<Equibles.Media.BusinessLogic.FileManager>();
builder.Services.AutoWireServicesFrom<Equibles.Sec.BusinessLogic.SecDocumentHtmlNormalizer>();
builder.Services.AutoWireServicesFrom<Equibles.Integrations.Sec.SecEdgarClient>();
builder.Services.AutoWireServicesFrom<Equibles.Integrations.Finra.FinraClient>();
builder.Services.AutoWireServicesFrom<Equibles.Integrations.Fred.FredClient>();
builder.Services.AutoWireServicesFrom<Equibles.Integrations.Yahoo.YahooFinanceClient>();
builder.Services.AutoWireServicesFrom<Equibles.Fred.HostedService.Services.FredImportService>();
builder.Services.AutoWireServicesFrom<Equibles.Sec.HostedService.Services.DocumentManager>();
builder.Services.AutoWireServicesFrom<Equibles.Congress.HostedService.Services.CongressionalTradeSyncService>();
builder.Services.AutoWireServicesFrom<Equibles.Holdings.HostedService.Services.HoldingsDataSetClient>();
builder.Services.AutoWireServicesFrom<Equibles.Sec.HostedService.Services.FtdImportService>();
builder.Services.AutoWireServicesFrom<Equibles.Finra.HostedService.Services.ShortVolumeImportService>();
builder.Services.AutoWireServicesFrom<Equibles.Yahoo.HostedService.Services.YahooPriceImportService>();
builder.Services.AutoWireServicesFrom<Equibles.Integrations.Cftc.CftcClient>();
builder.Services.AutoWireServicesFrom<Equibles.Integrations.Cboe.CboeClient>();
builder.Services.AutoWireServicesFrom<Equibles.Cftc.HostedService.Services.CftcImportService>();
builder.Services.AutoWireServicesFrom<Equibles.Cboe.HostedService.Services.CboeImportService>();

// Cross-module services (interface-based, need manual registration)
builder.Services.AddScoped<Equibles.Core.Contracts.IStockPriceProvider, Equibles.Yahoo.Repositories.YahooStockPriceProvider>();

// SEC scraper services (interface-based, need manual registration)
builder.Services.AddScoped<IFilingProcessor, InsiderTradingFilingProcessor>();
builder.Services.AddScoped<IDocumentPersistenceService, DocumentPersistenceService>();
builder.Services.AddScoped<ICompanySyncService, CompanySyncService>();
builder.Services.AddScoped<IDocumentScraper, DocumentScraper>();

builder.Services.AddHostedService<SecScraperWorker>();
builder.Services.AddHostedService<DocumentProcessorWorker>();
builder.Services.AddHostedService<HoldingsScraperWorker>();
builder.Services.AddHostedService<CongressionalTradeScraperWorker>();
builder.Services.AddHostedService<FtdScraperWorker>();
builder.Services.AddHostedService<FinraScraperWorker>();
builder.Services.AddHostedService<FredScraperWorker>();
builder.Services.AddHostedService<YahooPriceScraperWorker>();
builder.Services.AddHostedService<CftcScraperWorker>();
builder.Services.AddHostedService<CboeScraperWorker>();

var host = builder.Build();
host.Run();
