using Equibles.Cboe.Data;
using Equibles.Cboe.Repositories;
using Equibles.Cftc.Data;
using Equibles.Cftc.Repositories;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Congress.Data;
using Equibles.Congress.Repositories;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data;
using Equibles.Errors.Data.Models;
using Equibles.Errors.Repositories;
using Equibles.Finra.Data;
using Equibles.Finra.Repositories;
using Equibles.Fred.Data;
using Equibles.Fred.Data.Models;
using Equibles.Fred.Repositories;
using Equibles.Holdings.Data;
using Equibles.Holdings.Repositories;
using Equibles.InsiderTrading.Data;
using Equibles.InsiderTrading.Repositories;
using Equibles.Media.Data;
using Equibles.Sec.Repositories;
using Equibles.Tests.Helpers;
using Equibles.Web.Controllers;
using Equibles.Web.FlashMessage.Contracts;
using Equibles.Web.Models;
using Equibles.Web.Services;
using Equibles.Web.ViewModels.EconomicData;
using Equibles.Web.ViewModels.Stocks;
using Equibles.Yahoo.Data;
using Equibles.Yahoo.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.Tests.Web;

// ═══════════════════════════════════════════════════════════════════════
// HomeController Tests
// ═══════════════════════════════════════════════════════════════════════

public class HomeControllerTests {
    private readonly ILogger<HomeController> _logger = Substitute.For<ILogger<HomeController>>();
    private readonly IConfiguration _configuration = Substitute.For<IConfiguration>();

    private HomeController CreateController() {
        var controller = new HomeController(_logger, _configuration);
        controller.ControllerContext = new ControllerContext {
            HttpContext = new DefaultHttpContext()
        };
        controller.TempData = Substitute.For<ITempDataDictionary>();
        return controller;
    }

    // ── Index ───────────────────────────────────────────────────────────

    [Fact]
    public void Index_ReturnsViewResult() {
        var controller = CreateController();

        var result = controller.Index();

        result.Should().BeOfType<ViewResult>();
    }

    [Fact]
    public void Index_SetsTitle() {
        var controller = CreateController();

        controller.Index();

        controller.ViewData["Title"].Should().Be("Equibles — Open-Source Financial Data Platform");
    }

    // ── Error ───────────────────────────────────────────────────────────

    [Fact]
    public void Error_404_ReturnsViewResult() {
        var controller = CreateController();

        var result = controller.Error(404);

        result.Should().BeOfType<ViewResult>();
    }

    [Fact]
    public void Error_404_SetsTitleToPageNotFound() {
        var controller = CreateController();

        controller.Error(404);

        controller.ViewData["Title"].Should().Be("Page Not Found");
    }

    [Fact]
    public void Error_404_SetsDescriptionForMissingPage() {
        var controller = CreateController();

        controller.Error(404);

        controller.ViewData["Description"].Should().Be("The page you're looking for doesn't exist or has been moved.");
    }

    [Fact]
    public void Error_404_SetsResponseStatusCode() {
        var controller = CreateController();

        controller.Error(404);

        controller.Response.StatusCode.Should().Be(404);
    }

    [Fact]
    public void Error_429_SetsTitleToTooManyRequests() {
        var controller = CreateController();

        controller.Error(429);

        controller.ViewData["Title"].Should().Be("Too Many Requests");
    }

    [Fact]
    public void Error_429_SetsDescriptionForRateLimiting() {
        var controller = CreateController();

        controller.Error(429);

        controller.ViewData["Description"].Should().Be("You've made too many requests. Please wait a moment and try again.");
    }

    [Fact]
    public void Error_429_SetsResponseStatusCode() {
        var controller = CreateController();

        controller.Error(429);

        controller.Response.StatusCode.Should().Be(429);
    }

    [Fact]
    public void Error_500_SetsTitleToSomethingWentWrong() {
        var controller = CreateController();

        controller.Error(500);

        controller.ViewData["Title"].Should().Be("Something Went Wrong");
    }

    [Fact]
    public void Error_NullStatusCode_DefaultsTo500() {
        var controller = CreateController();

        controller.Error(null);

        controller.Response.StatusCode.Should().Be(500);
        controller.ViewData["Title"].Should().Be("Something Went Wrong");
    }

    // ── Connect ─────────────────────────────────────────────────────────

    [Fact]
    public void Connect_ReturnsViewResult() {
        var controller = CreateController();
        controller.HttpContext.Request.Scheme = "http";
        controller.HttpContext.Request.Host = new HostString("localhost", 5000);

        var result = controller.Connect();

        result.Should().BeOfType<ViewResult>();
    }

    [Fact]
    public void Connect_SetsTitleInViewData() {
        var controller = CreateController();
        controller.HttpContext.Request.Scheme = "http";
        controller.HttpContext.Request.Host = new HostString("localhost", 5000);

        controller.Connect();

        controller.ViewData["Title"].Should().Be("Connect AI Assistant");
    }

    [Fact]
    public void Connect_SetsMcpUrlFromConfiguration() {
        _configuration["McpPort"].Returns("9090");
        var controller = CreateController();
        controller.HttpContext.Request.Scheme = "https";
        controller.HttpContext.Request.Host = new HostString("myserver.com", 443);

        controller.Connect();

        controller.ViewData["McpUrl"].Should().Be("https://myserver.com:9090/mcp");
    }

    [Fact]
    public void Connect_DefaultsMcpPortTo8081WhenNotConfigured() {
        _configuration["McpPort"].Returns((string)null);
        var controller = CreateController();
        controller.HttpContext.Request.Scheme = "http";
        controller.HttpContext.Request.Host = new HostString("localhost", 5000);

        controller.Connect();

        ((string)controller.ViewData["McpUrl"]).Should().Contain(":8081/mcp");
    }

    [Fact]
    public void Connect_SetsApiKeyFromConfiguration() {
        _configuration["McpApiKey"].Returns("test-key-123");
        var controller = CreateController();
        controller.HttpContext.Request.Scheme = "http";
        controller.HttpContext.Request.Host = new HostString("localhost", 5000);

        controller.Connect();

        controller.ViewData["ApiKey"].Should().Be("test-key-123");
    }

    [Fact]
    public void Connect_DefaultsApiKeyToEmptyStringWhenNotConfigured() {
        _configuration["McpApiKey"].Returns((string)null);
        var controller = CreateController();
        controller.HttpContext.Request.Scheme = "http";
        controller.HttpContext.Request.Host = new HostString("localhost", 5000);

        controller.Connect();

        controller.ViewData["ApiKey"].Should().Be("");
    }
}

// ═══════════════════════════════════════════════════════════════════════
// StocksController Tests
// ═══════════════════════════════════════════════════════════════════════

public class StocksControllerTests : IDisposable {
    private readonly EquiblesDbContext _dbContext;
    private readonly CommonStockRepository _commonStockRepository;
    private readonly InstitutionalHolderRepository _institutionalHolderRepository;
    private readonly DocumentRepository _documentRepository;
    private readonly StockTabService _stockTabService;
    private readonly ILogger<StocksController> _logger = Substitute.For<ILogger<StocksController>>();

    public StocksControllerTests() {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new MediaModuleConfiguration(),
            new SecTestModuleConfiguration(),
            new HoldingsModuleConfiguration(),
            new FinraModuleConfiguration(),
            new InsiderTradingModuleConfiguration(),
            new CongressModuleConfiguration(),
            new YahooModuleConfiguration()
        );

        _commonStockRepository = new CommonStockRepository(_dbContext);
        _institutionalHolderRepository = new InstitutionalHolderRepository(_dbContext);
        _documentRepository = new DocumentRepository(_dbContext);
        _stockTabService = new StockTabService(
            new InstitutionalHoldingRepository(_dbContext),
            _institutionalHolderRepository,
            new DailyShortVolumeRepository(_dbContext),
            new ShortInterestRepository(_dbContext),
            new FailToDeliverRepository(_dbContext),
            _documentRepository,
            new InsiderTransactionRepository(_dbContext),
            new CongressionalTradeRepository(_dbContext),
            new DailyStockPriceRepository(_dbContext)
        );
    }

    public void Dispose() {
        _dbContext.Dispose();
    }

    private StocksController CreateController() {
        var controller = new StocksController(
            _commonStockRepository,
            _institutionalHolderRepository,
            _documentRepository,
            _stockTabService,
            _logger
        );
        controller.ControllerContext = new ControllerContext {
            HttpContext = new DefaultHttpContext()
        };
        controller.TempData = Substitute.For<ITempDataDictionary>();
        return controller;
    }

    private CommonStock SeedStock(string ticker = "AAPL", string name = "Apple Inc.") {
        var stock = new CommonStock {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = name,
            Cik = Guid.NewGuid().ToString()[..10],
        };
        _dbContext.Set<CommonStock>().Add(stock);
        _dbContext.SaveChanges();
        return stock;
    }

    // ── Index ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Index_ReturnsViewResult() {
        var controller = CreateController();

        var result = await controller.Index(null);

        result.Should().BeOfType<ViewResult>();
    }

    [Fact]
    public async Task Index_SetsTitle() {
        var controller = CreateController();

        await controller.Index(null);

        controller.ViewData["Title"].Should().Be("Stocks");
    }

    [Fact]
    public async Task Index_EmptyDatabase_ReturnsEmptyStockList() {
        var controller = CreateController();

        var result = await controller.Index(null);

        var viewResult = result.As<ViewResult>();
        var model = viewResult.Model.As<StockBrowserViewModel>();
        model.Stocks.Should().BeEmpty();
        model.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Index_WithStocks_ReturnsPopulatedViewModel() {
        SeedStock("AAPL", "Apple Inc.");
        SeedStock("MSFT", "Microsoft Corp.");
        var controller = CreateController();

        var result = await controller.Index(null);

        var viewResult = result.As<ViewResult>();
        var model = viewResult.Model.As<StockBrowserViewModel>();
        model.Stocks.Should().HaveCount(2);
        model.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task Index_WithPagination_RespectsPageSize() {
        for (int i = 0; i < 55; i++) {
            SeedStock($"T{i:D3}", $"Stock {i}");
        }

        var controller = CreateController();

        var result = await controller.Index(null, page: 2);

        var model = result.As<ViewResult>().Model.As<StockBrowserViewModel>();
        model.Page.Should().Be(2);
        model.Stocks.Should().HaveCount(5);
        model.TotalCount.Should().Be(55);
    }

    // ── Show (redirect) ─────────────────────────────────────────────────

    [Fact]
    public void Show_RedirectsToPriceAction() {
        var controller = CreateController();

        var result = controller.Show("AAPL");

        result.Should().BeOfType<RedirectToActionResult>();
        var redirect = result.As<RedirectToActionResult>();
        redirect.ActionName.Should().Be("Price");
    }

    [Fact]
    public void Show_PassesTickerInRouteValues() {
        var controller = CreateController();

        var result = controller.Show("msft");

        var redirect = result.As<RedirectToActionResult>();
        redirect.RouteValues.Should().ContainKey("ticker");
        redirect.RouteValues["ticker"].Should().Be("msft");
    }

    // ── Price (valid stock) ─────────────────────────────────────────────

    [Fact]
    public async Task Price_ValidStock_ReturnsViewResult() {
        SeedStock("AAPL", "Apple Inc.");
        var controller = CreateController();

        var result = await controller.Price("AAPL");

        result.Should().BeOfType<ViewResult>();
    }

    [Fact]
    public async Task Price_ValidStock_SetsTitle() {
        SeedStock("AAPL", "Apple Inc.");
        var controller = CreateController();

        await controller.Price("AAPL");

        controller.ViewData["Title"].Should().Be("AAPL - Apple Inc.");
    }

    [Fact]
    public async Task Price_ValidStock_UsesShowViewName() {
        SeedStock("AAPL", "Apple Inc.");
        var controller = CreateController();

        var result = await controller.Price("AAPL");

        var viewResult = result.As<ViewResult>();
        viewResult.ViewName.Should().Be("Show");
    }

    [Fact]
    public async Task Price_ValidStock_SetsActiveTabToPrice() {
        SeedStock("AAPL", "Apple Inc.");
        var controller = CreateController();

        var result = await controller.Price("AAPL");

        var model = result.As<ViewResult>().Model.As<StockDetailViewModel>();
        model.ActiveTab.Should().Be("price");
    }

    [Fact]
    public async Task Price_ValidStock_SetsTabViewModel() {
        SeedStock("AAPL", "Apple Inc.");
        var controller = CreateController();

        await controller.Price("AAPL");

        controller.ViewData["TabViewModel"].Should().BeOfType<PriceTabViewModel>();
    }

    // ── Price (missing stock) ───────────────────────────────────────────

    [Fact]
    public async Task Price_MissingStock_ReturnsNotFound() {
        var controller = CreateController();

        var result = await controller.Price("NONEXISTENT");

        result.Should().BeOfType<NotFoundResult>();
    }

    // ── Price (case insensitivity) ──────────────────────────────────────

    [Fact]
    public async Task Price_LowercaseTicker_FindsStock() {
        SeedStock("AAPL", "Apple Inc.");
        var controller = CreateController();

        var result = await controller.Price("aapl");

        result.Should().BeOfType<ViewResult>();
    }
}

// ═══════════════════════════════════════════════════════════════════════
// EconomicDataController Tests
// ═══════════════════════════════════════════════════════════════════════

public class EconomicDataControllerTests : IDisposable {
    private readonly EquiblesDbContext _dbContext;
    private readonly FredSeriesRepository _seriesRepository;
    private readonly FredObservationRepository _observationRepository;
    private readonly ILogger<EconomicDataController> _logger = Substitute.For<ILogger<EconomicDataController>>();

    public EconomicDataControllerTests() {
        _dbContext = TestDbContextFactory.Create(
            new FredModuleConfiguration()
        );

        _seriesRepository = new FredSeriesRepository(_dbContext);
        _observationRepository = new FredObservationRepository(_dbContext);
    }

    public void Dispose() {
        _dbContext.Dispose();
    }

    private EconomicDataController CreateController() {
        var controller = new EconomicDataController(
            _seriesRepository,
            _observationRepository,
            _logger
        );
        controller.ControllerContext = new ControllerContext {
            HttpContext = new DefaultHttpContext()
        };
        controller.TempData = Substitute.For<ITempDataDictionary>();
        return controller;
    }

    private FredSeries SeedSeries(
        string seriesId = "GDP",
        string title = "Gross Domestic Product",
        FredSeriesCategory category = FredSeriesCategory.GdpAndOutput,
        string frequency = "Q",
        string units = "Billions of Dollars") {
        var series = new FredSeries {
            Id = Guid.NewGuid(),
            SeriesId = seriesId,
            Title = title,
            Category = category,
            Frequency = frequency,
            Units = units,
        };
        _dbContext.Set<FredSeries>().Add(series);
        _dbContext.SaveChanges();
        return series;
    }

    private void SeedObservation(FredSeries series, DateOnly date, decimal? value) {
        _dbContext.Set<FredObservation>().Add(new FredObservation {
            Id = Guid.NewGuid(),
            FredSeriesId = series.Id,
            Date = date,
            Value = value,
        });
        _dbContext.SaveChanges();
    }

    // ── Index ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Index_ReturnsViewResult() {
        var controller = CreateController();

        var result = await controller.Index();

        result.Should().BeOfType<ViewResult>();
    }

    [Fact]
    public async Task Index_EmptyDatabase_ReturnsEmptyCategories() {
        var controller = CreateController();

        var result = await controller.Index();

        var model = result.As<ViewResult>().Model.As<EconomyIndexViewModel>();
        model.Categories.Should().BeEmpty();
    }

    [Fact]
    public async Task Index_WithSeries_GroupsByCategory() {
        SeedSeries("GDP", "Gross Domestic Product", FredSeriesCategory.GdpAndOutput);
        SeedSeries("FEDFUNDS", "Federal Funds Rate", FredSeriesCategory.InterestRates);
        var controller = CreateController();

        var result = await controller.Index();

        var model = result.As<ViewResult>().Model.As<EconomyIndexViewModel>();
        model.Categories.Should().HaveCount(2);
    }

    [Fact]
    public async Task Index_WithSeries_IncludesLatestObservationValue() {
        var series = SeedSeries("GDP", "Gross Domestic Product");
        SeedObservation(series, new DateOnly(2024, 1, 1), 27_000m);
        SeedObservation(series, new DateOnly(2024, 4, 1), 28_000m);
        var controller = CreateController();

        var result = await controller.Index();

        var model = result.As<ViewResult>().Model.As<EconomyIndexViewModel>();
        var item = model.Categories.SelectMany(c => c.Series).First(s => s.SeriesId == "GDP");
        item.LatestValue.Should().Be(28_000m);
        item.LatestDate.Should().Be(new DateOnly(2024, 4, 1));
    }

    // ── Show (valid series) ─────────────────────────────────────────────

    [Fact]
    public async Task Show_ValidSeries_ReturnsViewResult() {
        var series = SeedSeries("GDP", "Gross Domestic Product");
        SeedObservation(series, new DateOnly(2024, 1, 1), 27_000m);
        SeedObservation(series, new DateOnly(2024, 4, 1), 28_000m);
        var controller = CreateController();

        var result = await controller.Show("GDP");

        result.Should().BeOfType<ViewResult>();
    }

    [Fact]
    public async Task Show_ValidSeries_SetsTitle() {
        var series = SeedSeries("GDP", "Gross Domestic Product");
        SeedObservation(series, new DateOnly(2024, 1, 1), 27_000m);
        SeedObservation(series, new DateOnly(2024, 4, 1), 28_000m);
        var controller = CreateController();

        await controller.Show("GDP");

        controller.ViewData["Title"].Should().Be("GDP — Gross Domestic Product");
    }

    [Fact]
    public async Task Show_ValidSeries_SetsDescription() {
        var series = SeedSeries("GDP", "Gross Domestic Product", units: "Billions of Dollars");
        SeedObservation(series, new DateOnly(2024, 1, 1), 27_000m);
        SeedObservation(series, new DateOnly(2024, 4, 1), 28_000m);
        var controller = CreateController();

        await controller.Show("GDP");

        ((string)controller.ViewData["Description"]).Should().Contain("GDP");
        ((string)controller.ViewData["Description"]).Should().Contain("Billions of Dollars");
    }

    [Fact]
    public async Task Show_ValidSeries_PopulatesObservations() {
        var series = SeedSeries("GDP", "Gross Domestic Product");
        SeedObservation(series, new DateOnly(2024, 1, 1), 27_000m);
        SeedObservation(series, new DateOnly(2024, 4, 1), 28_000m);
        var controller = CreateController();

        var result = await controller.Show("GDP");

        var model = result.As<ViewResult>().Model.As<EconomySeriesViewModel>();
        model.Observations.Should().HaveCount(2);
        model.SeriesId.Should().Be("GDP");
        model.Title.Should().Be("Gross Domestic Product");
    }

    [Fact]
    public async Task Show_ValidSeries_ComputesStatistics() {
        var series = SeedSeries("GDP", "Gross Domestic Product");
        SeedObservation(series, new DateOnly(2024, 1, 1), 100m);
        SeedObservation(series, new DateOnly(2024, 4, 1), 200m);
        SeedObservation(series, new DateOnly(2024, 7, 1), 300m);
        var controller = CreateController();

        var result = await controller.Show("GDP");

        var model = result.As<ViewResult>().Model.As<EconomySeriesViewModel>();
        model.Mean.Should().Be(200m);
        model.Min.Should().Be(100m);
        model.Max.Should().Be(300m);
        model.Median.Should().Be(200m);
        model.LatestValue.Should().Be(300m);
        model.PreviousValue.Should().Be(200m);
    }

    [Fact]
    public async Task Show_ValidSeries_ExpandsFrequency() {
        SeedSeries("GDP", "Gross Domestic Product", frequency: "Q");
        var controller = CreateController();

        var result = await controller.Show("GDP");

        var model = result.As<ViewResult>().Model.As<EconomySeriesViewModel>();
        model.Frequency.Should().Be("Quarterly");
    }

    [Fact]
    public async Task Show_CaseInsensitiveSeriesId_FindsSeries() {
        var series = SeedSeries("GDP", "Gross Domestic Product");
        SeedObservation(series, new DateOnly(2024, 1, 1), 27_000m);
        SeedObservation(series, new DateOnly(2024, 4, 1), 28_000m);
        var controller = CreateController();

        var result = await controller.Show("gdp");

        result.Should().BeOfType<ViewResult>();
    }

    // ── Show (missing series) ───────────────────────────────────────────

    [Fact]
    public async Task Show_MissingSeries_ReturnsNotFound() {
        var controller = CreateController();

        var result = await controller.Show("NONEXISTENT");

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Show_NullSeriesId_ReturnsNotFound() {
        var controller = CreateController();

        var result = await controller.Show(null);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Show_EmptySeriesId_ReturnsNotFound() {
        var controller = CreateController();

        var result = await controller.Show("");

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Show_WhitespaceSeriesId_ReturnsNotFound() {
        var controller = CreateController();

        var result = await controller.Show("   ");

        result.Should().BeOfType<NotFoundResult>();
    }
}

// ═══════════════════════════════════════════════════════════════════════
// StatusController Tests
// ═══════════════════════════════════════════════════════════════════════

public class StatusControllerTests : IDisposable {
    private readonly EquiblesDbContext _dbContext;
    private readonly ErrorRepository _errorRepository;
    private readonly ErrorManager _errorManager;
    private readonly IFlashMessage _flashMessage = Substitute.For<IFlashMessage>();
    private readonly Dictionary<string, string> _configValues = new();
    private readonly DataCountService _dataCountService;
    private readonly ILogger<StatusController> _logger = Substitute.For<ILogger<StatusController>>();

    public StatusControllerTests() {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new MediaModuleConfiguration(),
            new SecTestModuleConfiguration(),
            new HoldingsModuleConfiguration(),
            new FinraModuleConfiguration(),
            new InsiderTradingModuleConfiguration(),
            new CongressModuleConfiguration(),
            new FredModuleConfiguration(),
            new YahooModuleConfiguration(),
            new ErrorsModuleConfiguration(),
            new CftcModuleConfiguration(),
            new CboeModuleConfiguration()
        );

        _errorRepository = new ErrorRepository(_dbContext);
        _errorManager = new ErrorManager(_errorRepository);
        _dataCountService = new DataCountService(
            new CommonStockRepository(_dbContext),
            new DocumentRepository(_dbContext),
            new InsiderTransactionRepository(_dbContext),
            new CongressionalTradeRepository(_dbContext),
            new InstitutionalHoldingRepository(_dbContext),
            new FailToDeliverRepository(_dbContext),
            new FredObservationRepository(_dbContext),
            new DailyStockPriceRepository(_dbContext),
            new CftcPositionReportRepository(_dbContext),
            new CboePutCallRatioRepository(_dbContext),
            new CboeVixDailyRepository(_dbContext)
        );
    }

    public void Dispose() {
        _dbContext.Dispose();
    }

    private IConfiguration BuildConfiguration() {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(_configValues)
            .Build();
    }

    private StatusController CreateController() {
        var controller = new StatusController(
            _errorRepository,
            _errorManager,
            _flashMessage,
            BuildConfiguration(),
            _dbContext,
            _dataCountService,
            _logger
        );
        controller.ControllerContext = new ControllerContext {
            HttpContext = new DefaultHttpContext()
        };
        controller.TempData = Substitute.For<ITempDataDictionary>();
        return controller;
    }

    private void SeedError(string context = "TestContext", string message = "Test error") {
        _dbContext.Set<Error>().Add(new Error {
            Id = Guid.NewGuid(),
            Source = ErrorSource.Other,
            Context = context,
            Message = message,
        });
        _dbContext.SaveChanges();
    }

    // ── Index ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Index_ReturnsViewResult() {
        var controller = CreateController();

        var result = await controller.Index(null, null);

        result.Should().BeOfType<ViewResult>();
    }

    [Fact]
    public void OnActionExecuting_SetsMenuAndTitleViewData() {
        var controller = CreateController();

        var actionContext = new ActionContext(
            new DefaultHttpContext(),
            new Microsoft.AspNetCore.Routing.RouteData(),
            new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());
        var executingContext = new Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext(
            actionContext, [], new Dictionary<string, object>(), controller);

        controller.OnActionExecuting(executingContext);

        controller.ViewData["Menu"].Should().Be("Status");
        controller.ViewData["Title"].Should().Be("Status");
    }

    [Fact]
    public async Task Index_EmptyDatabase_ReturnsStatusWithZeroCounts() {
        var controller = CreateController();

        var result = await controller.Index(null, null);

        var model = result.As<ViewResult>().Model.As<SystemStatusViewModel>();
        model.DatabaseConnected.Should().BeTrue();
        model.StockCount.Should().Be(0);
        model.DocumentCount.Should().Be(0);
        model.TotalErrorCount.Should().Be(0);
        model.UnseenErrorCount.Should().Be(0);
    }

    [Fact]
    public async Task Index_WithErrors_IncludesRecentErrors() {
        SeedError("Context1", "Error 1");
        SeedError("Context2", "Error 2");
        var controller = CreateController();

        var result = await controller.Index(null, null);

        var model = result.As<ViewResult>().Model.As<SystemStatusViewModel>();
        model.RecentErrors.Should().HaveCount(2);
        model.TotalErrorCount.Should().Be(2);
    }

    [Fact]
    public async Task Index_SetsSearchViewData() {
        var controller = CreateController();

        await controller.Index("test-search", null);

        controller.ViewData["Search"].Should().Be("test-search");
    }

    [Fact]
    public async Task Index_SetsSourceFilterViewData() {
        var controller = CreateController();

        await controller.Index(null, "McpTool");

        controller.ViewData["SourceFilter"].Should().Be("McpTool");
    }

    [Fact]
    public async Task Index_IncludesWorkerStatuses() {
        var controller = CreateController();

        var result = await controller.Index(null, null);

        var model = result.As<ViewResult>().Model.As<SystemStatusViewModel>();
        model.Workers.Should().NotBeEmpty();
        model.Workers.Should().Contain(w => w.Name == "Congressional Trade Scraper" && w.Active);
        model.Workers.Should().Contain(w => w.Name == "Yahoo Price Scraper" && w.Active);
    }

    [Fact]
    public async Task Index_McpApiKeyNotConfigured_ReportsFalse() {
        // McpApiKey not in config => null => McpApiKeyConfigured = false
        var controller = CreateController();

        var result = await controller.Index(null, null);

        var model = result.As<ViewResult>().Model.As<SystemStatusViewModel>();
        model.McpApiKeyConfigured.Should().BeFalse();
    }

    [Fact]
    public async Task Index_McpApiKeyConfigured_ReportsTrue() {
        _configValues["McpApiKey"] = "my-api-key";
        var controller = CreateController();

        var result = await controller.Index(null, null);

        var model = result.As<ViewResult>().Model.As<SystemStatusViewModel>();
        model.McpApiKeyConfigured.Should().BeTrue();
    }

    // ── Data (JSON endpoint) ────────────────────────────────────────────

    [Fact]
    public async Task Data_ReturnsJsonResult() {
        var controller = CreateController();

        var result = await controller.Data();

        result.Should().BeOfType<JsonResult>();
    }

    [Fact]
    public async Task Data_EmptyDatabase_IncludesDatabaseConnectedTrue() {
        var controller = CreateController();

        var result = await controller.Data();

        var jsonResult = result.As<JsonResult>();
        var value = jsonResult.Value;
        value.Should().NotBeNull();

        // Use reflection to check the anonymous object properties
        var dbConnected = value.GetType().GetProperty("DatabaseConnected")?.GetValue(value);
        dbConnected.Should().Be(true);
    }

    [Fact]
    public async Task Data_EmptyDatabase_IncludesZeroDataCounts() {
        var controller = CreateController();

        var result = await controller.Data();

        var jsonResult = result.As<JsonResult>();
        var value = jsonResult.Value;

        var dataCounts = value.GetType().GetProperty("DataCounts")?.GetValue(value) as Dictionary<string, int>;
        dataCounts.Should().NotBeNull();
        dataCounts["StockCount"].Should().Be(0);
        dataCounts["DocumentCount"].Should().Be(0);
    }

    [Fact]
    public async Task Data_IncludesWorkerInfo() {
        var controller = CreateController();

        var result = await controller.Data();

        var value = result.As<JsonResult>().Value;
        var totalWorkerCount = (int)value.GetType().GetProperty("TotalWorkerCount")?.GetValue(value);
        totalWorkerCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Data_IncludesErrorCounts() {
        SeedError();
        var controller = CreateController();

        var result = await controller.Data();

        var value = result.As<JsonResult>().Value;
        var totalErrorCount = (int)value.GetType().GetProperty("TotalErrorCount")?.GetValue(value);
        var unseenErrorCount = (int)value.GetType().GetProperty("UnseenErrorCount")?.GetValue(value);
        totalErrorCount.Should().Be(1);
        unseenErrorCount.Should().Be(1);
    }
}
