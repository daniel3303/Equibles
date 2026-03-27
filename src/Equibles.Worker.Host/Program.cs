using Equibles.CommonStocks.Data.Extensions;
using Equibles.Congress.Data.Extensions;
using Equibles.Congress.HostedService;
using Equibles.Core.AutoWiring;
using Equibles.Data.Extensions;
using Equibles.Errors.Data.Extensions;
using Equibles.Holdings.Data.Extensions;
using Equibles.Holdings.HostedService;
using Equibles.InsiderTrading.Data.Extensions;
using Equibles.Media.Data.Extensions;
using Equibles.Sec.Data.Extensions;
using Equibles.Sec.HostedService;
using Equibles.Sec.HostedService.Configuration;
using Equibles.Sec.HostedService.Contracts;
using Equibles.Sec.HostedService.Services;
using Equibles.Fred.Data.Extensions;
using Equibles.Fred.HostedService;
using Equibles.ShortData.Data.Extensions;
using Equibles.ShortData.HostedService;
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
});

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

builder.Services.Configure<DocumentScraperOptions>(
    builder.Configuration.GetSection("DocumentScraper"));
builder.Services.Configure<Equibles.Congress.HostedService.Configuration.CongressScraperOptions>(
    builder.Configuration.GetSection("CongressScraper"));
builder.Services.Configure<Equibles.Integrations.Finra.Configuration.FinraOptions>(
    builder.Configuration.GetSection("Finra"));
builder.Services.Configure<Equibles.Fred.HostedService.Configuration.FredScraperOptions>(
    builder.Configuration.GetSection("FredScraper"));
builder.Services.Configure<Equibles.Integrations.Fred.Configuration.FredOptions>(
    builder.Configuration.GetSection("Fred"));

builder.Services.AddHttpClient();

builder.Services.AutoWireServicesFrom<Equibles.Errors.BusinessLogic.ErrorManager>();
builder.Services.AutoWireServicesFrom<Equibles.CommonStocks.BusinessLogic.CommonStockManager>();
builder.Services.AutoWireServicesFrom<Equibles.Media.BusinessLogic.FileManager>();
builder.Services.AutoWireServicesFrom<Equibles.Sec.BusinessLogic.SecDocumentHtmlNormalizer>();
builder.Services.AutoWireServicesFrom<Equibles.Integrations.Sec.SecEdgarClient>();
builder.Services.AutoWireServicesFrom<Equibles.Integrations.Finra.FinraClient>();
builder.Services.AutoWireServicesFrom<Equibles.Integrations.Fred.FredClient>();
builder.Services.AutoWireServicesFrom<Equibles.Fred.HostedService.Services.FredImportService>();
builder.Services.AutoWireServicesFrom<Equibles.Sec.HostedService.Services.DocumentManager>();
builder.Services.AutoWireServicesFrom<Equibles.Congress.HostedService.Services.CongressionalTradeSyncService>();
builder.Services.AutoWireServicesFrom<Equibles.Holdings.HostedService.Services.HoldingsDataSetClient>();
builder.Services.AutoWireServicesFrom<Equibles.ShortData.HostedService.Services.FtdImportService>();

// SEC scraper services (interface-based, need manual registration)
builder.Services.AddScoped<IFilingProcessor, InsiderTradingFilingProcessor>();
builder.Services.AddScoped<IDocumentPersistenceService, DocumentPersistenceService>();
builder.Services.AddScoped<ICompanySyncService, CompanySyncService>();
builder.Services.AddScoped<IDocumentScraper, DocumentScraper>();

builder.Services.AddHostedService<SecScraperWorker>();
builder.Services.AddHostedService<DocumentProcessorWorker>();
builder.Services.AddHostedService<HoldingsScraperWorker>();
builder.Services.AddHostedService<CongressionalTradeScraperWorker>();
builder.Services.AddHostedService<ShortDataScraperWorker>();
builder.Services.AddHostedService<FredScraperWorker>();

var host = builder.Build();
host.Run();
