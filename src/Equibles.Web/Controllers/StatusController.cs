using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Errors.Repositories;
using Equibles.Messaging.Contracts.Activity;
using Equibles.Web.Controllers.Abstract;
using Equibles.Web.FlashMessage.Contracts;
using Equibles.Web.Models;
using Equibles.Web.Services;
using Equibles.Web.Services.Activity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Web.Controllers;

public class StatusController : BaseController
{
    private readonly ErrorRepository _errorRepository;
    private readonly ErrorManager _errorManager;
    private readonly IFlashMessage _flashMessage;
    private readonly IConfiguration _configuration;
    private readonly EquiblesDbContext _dbContext;
    private readonly DataCountService _dataCountService;
    private readonly ActivityFeedBroadcaster _activityFeed;

    public StatusController(
        ErrorRepository errorRepository,
        ErrorManager errorManager,
        IFlashMessage flashMessage,
        IConfiguration configuration,
        EquiblesDbContext dbContext,
        DataCountService dataCountService,
        ActivityFeedBroadcaster activityFeed,
        ILogger<StatusController> logger
    )
        : base(logger)
    {
        _errorRepository = errorRepository;
        _errorManager = errorManager;
        _flashMessage = flashMessage;
        _configuration = configuration;
        _dbContext = dbContext;
        _dataCountService = dataCountService;
        _activityFeed = activityFeed;
    }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        base.OnActionExecuting(context);
        ViewData["Menu"] = "Status";
        ViewData["Title"] = "Status";
    }

    [HttpGet]
    public async Task<IActionResult> Index(string search, string source)
    {
        var status = await BuildStatus();

        // MCP API key
        status.McpApiKeyConfigured = !string.IsNullOrEmpty(_configuration["McpApiKey"]);

        // Filtered errors for the table
        var query = _errorRepository.Search(search);
        if (!string.IsNullOrEmpty(source))
        {
            var errorSource = new ErrorSource(source);
            query = query.Where(e => e.Source == errorSource);
        }

        status.RecentErrors = await query
            .OrderByDescending(e => e.CreationTime)
            .Take(100)
            .ToListAsync();

        ViewData["Search"] = search;
        ViewData["SourceFilter"] = source;
        return View(status);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Show(Guid id)
    {
        var error = await LoadErrorOrFlashNotFound(id);
        if (error == null)
            return RedirectToAction(nameof(Index));

        return View(error);
    }

    [HttpPost("{id:guid}/MarkAsSeen")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAsSeen(Guid id)
    {
        var error = await LoadErrorOrFlashNotFound(id);
        if (error == null)
            return RedirectToAction(nameof(Index));

        await _errorManager.MarkAsSeen(error);

        _flashMessage.Success("Error marked as seen.");
        return RedirectToAction(nameof(Show), new { id });
    }

    [HttpPost("{id:guid}/Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var error = await LoadErrorOrFlashNotFound(id);
        if (error == null)
            return RedirectToAction(nameof(Index));

        await _errorManager.Delete(error);

        _flashMessage.Success("Error deleted.");
        return RedirectToAction(nameof(Index));
    }

    private async Task<Error> LoadErrorOrFlashNotFound(Guid id)
    {
        var error = await _errorRepository.Get(id);
        if (error == null)
            _flashMessage.Error("Error not found.");
        return error;
    }

    [HttpGet]
    public async Task<IActionResult> Data()
    {
        var status = await BuildStatus();
        var workers = status.Workers;

        return Json(
            new
            {
                status.DatabaseConnected,
                DataCounts = new Dictionary<string, int>
                {
                    ["StockCount"] = status.StockCount,
                    ["DocumentCount"] = status.DocumentCount,
                    ["InsiderTransactionCount"] = status.InsiderTransactionCount,
                    ["CongressionalTradeCount"] = status.CongressionalTradeCount,
                    ["InstitutionalHoldingCount"] = status.InstitutionalHoldingCount,
                    ["FailToDeliverCount"] = status.FailToDeliverCount,
                    ["FredObservationCount"] = status.FredObservationCount,
                    ["DailyStockPriceCount"] = status.DailyStockPriceCount,
                    ["CftcPositionReportCount"] = status.CftcPositionReportCount,
                    ["CboePutCallRatioCount"] = status.CboePutCallRatioCount,
                    ["CboeVixDailyCount"] = status.CboeVixDailyCount,
                },
                Workers = workers.Select(w => new
                {
                    w.Name,
                    w.Active,
                    w.Reason,
                }),
                status.TotalErrorCount,
                status.UnseenErrorCount,
                ActiveWorkerCount = workers.Count(w => w.Active),
                TotalWorkerCount = workers.Count,
            }
        );
    }

    [HttpGet("~/Status/Activity/Stream")]
    public async Task ActivityStream(CancellationToken cancellationToken)
    {
        InitSseStream();

        // Hint to load balancers / browsers that the stream is live.
        Response.Headers["X-Activity-Stream"] = "scraper";

        // Flush the response headers immediately. Without this the headers
        // sit in the buffer until the first SSE frame is written; that's
        // fine for a noisy feed but a quiet one leaves the browser waiting
        // forever for the connection to "open".
        await Response.Body.FlushAsync(cancellationToken);

        using var subscription = _activityFeed.Subscribe();

        // Replay the small ring buffer so a freshly-opened tab has context,
        // then forward live events until the client disconnects.
        foreach (var activity in subscription.Backlog)
        {
            await WriteActivityEvent(activity);
        }

        try
        {
            await foreach (var activity in subscription.Reader.ReadAllAsync(cancellationToken))
            {
                await WriteActivityEvent(activity);
            }
        }
        catch (OperationCanceledException)
        {
            // Browser closed the tab — clean disconnect.
        }
    }

    private Task WriteActivityEvent(ScraperActivity activity) =>
        WriteSseEvent(
            "activity",
            new
            {
                activity.Source,
                Severity = activity.Severity.ToString(),
                activity.Message,
                activity.Timestamp,
                activity.CorrelationId,
            }
        );

    [HttpPost("DeleteAll")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAll()
    {
        await _errorManager.DeleteAll();

        _flashMessage.Success("All errors deleted.");
        return RedirectToAction(nameof(Index));
    }

    private async Task<SystemStatusViewModel> BuildStatus()
    {
        var status = new SystemStatusViewModel();

        try
        {
            await _dbContext.Database.CanConnectAsync();
            status.DatabaseConnected = true;
        }
        catch
        {
            status.DatabaseConnected = false;
        }

        if (status.DatabaseConnected)
        {
            status.StockCount = await _dataCountService.GetStockCount();
            status.DocumentCount = await _dataCountService.GetDocumentCount();
            status.InsiderTransactionCount = await _dataCountService.GetInsiderTransactionCount();
            status.CongressionalTradeCount = await _dataCountService.GetCongressionalTradeCount();
            status.InstitutionalHoldingCount =
                await _dataCountService.GetInstitutionalHoldingCount();
            status.FailToDeliverCount = await _dataCountService.GetFailToDeliverCount();
            status.FredObservationCount = await _dataCountService.GetFredObservationCount();
            status.DailyStockPriceCount = await _dataCountService.GetDailyStockPriceCount();
            status.CftcPositionReportCount = await _dataCountService.GetCftcPositionReportCount();
            status.CboePutCallRatioCount = await _dataCountService.GetCboePutCallRatioCount();
            status.CboeVixDailyCount = await _dataCountService.GetCboeVixDailyCount();
        }

        status.Workers = BuildWorkerStatuses();
        status.TotalErrorCount = await _errorRepository.GetAll().CountAsync();
        status.UnseenErrorCount = await _errorRepository.GetAll().CountAsync(e => !e.Seen);

        return status;
    }

    private List<WorkerStatus> BuildWorkerStatuses()
    {
        var secConfigured = !string.IsNullOrEmpty(_configuration["Sec:ContactEmail"]);
        var finraConfigured = !string.IsNullOrEmpty(_configuration["Finra:ClientId"]);
        var fredConfigured = !string.IsNullOrEmpty(_configuration["Fred:ApiKey"]);
        var embeddingEnabled = _configuration.GetValue<bool>("Embedding:Enabled");
        var embeddingBaseUrl = _configuration["Embedding:BaseUrl"];
        var embeddingModel = _configuration["Embedding:ModelName"];

        return
        [
            Configurable(
                "SEC Scraper",
                "Filings (10-K, 10-Q, 8-K, Form 3/4), document processing, institutional holdings (13F-HR), and fails-to-deliver",
                secConfigured,
                "SEC contact email configured",
                "SEC_CONTACT_EMAIL not set — required by SEC EDGAR fair access policy"
            ),
            Configurable(
                "FINRA Scraper",
                "Daily short volume and short interest",
                finraConfigured,
                "FINRA API credentials configured",
                "Finra:ClientId not set — get a free key at gateway.finra.org/app/api-console"
            ),
            AlwaysActive(
                "Congressional Trade Scraper",
                "House and Senate stock trade disclosures",
                "Always active"
            ),
            Configurable(
                "FRED Scraper",
                "Economic indicators from the Federal Reserve (interest rates, inflation, employment, GDP)",
                fredConfigured,
                "FRED API key configured",
                "Fred:ApiKey not set — get a free key at fred.stlouisfed.org/docs/api/api_key.html"
            ),
            AlwaysActive(
                "Yahoo Price Scraper",
                "Daily OHLCV stock prices with technical indicators (SMA, RSI, MACD)"
            ),
            AlwaysActive("CFTC Scraper", "Commitments of Traders futures positioning data"),
            AlwaysActive("CBOE Scraper", "VIX volatility index and put/call ratios"),
            new WorkerStatus
            {
                Name = "Embedding Generator",
                Description = "Vector embeddings for semantic search over SEC filings",
                Active = embeddingEnabled && !string.IsNullOrEmpty(embeddingBaseUrl),
                Reason =
                    !embeddingEnabled ? "Disabled (Embedding:Enabled = false)"
                    : string.IsNullOrEmpty(embeddingBaseUrl)
                        ? "No embedding server configured (Embedding:BaseUrl)"
                    : $"Model: {embeddingModel ?? "not set"}, Server: {embeddingBaseUrl}",
            },
        ];
    }

    private static WorkerStatus Configurable(
        string name,
        string description,
        bool configured,
        string configuredReason,
        string unconfiguredReason
    ) =>
        new()
        {
            Name = name,
            Description = description,
            Active = configured,
            Reason = configured ? configuredReason : unconfiguredReason,
        };

    private static WorkerStatus AlwaysActive(
        string name,
        string description,
        string reason = "Always active — no API key required"
    ) =>
        new()
        {
            Name = name,
            Description = description,
            Active = true,
            Reason = reason,
        };
}
