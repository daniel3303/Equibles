using Equibles.CommonStocks.Data.Models;
using Equibles.Congress.Data.Models;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Errors.Repositories;
using Equibles.Holdings.Data.Models;
using Equibles.InsiderTrading.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Fred.Data.Models;
using Equibles.ShortData.Data.Models;
using Equibles.Web.Controllers.Abstract;
using Equibles.Web.FlashMessage.Contracts;
using Equibles.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Web.Controllers;

public class StatusController : BaseController {
    private readonly ErrorRepository _errorRepository;
    private readonly ErrorManager _errorManager;
    private readonly IFlashMessage _flashMessage;
    private readonly IConfiguration _configuration;
    private readonly EquiblesDbContext _dbContext;

    public StatusController(
        ErrorRepository errorRepository,
        ErrorManager errorManager,
        IFlashMessage flashMessage,
        IConfiguration configuration,
        EquiblesDbContext dbContext,
        ILogger<StatusController> logger
    ) : base(logger) {
        _errorRepository = errorRepository;
        _errorManager = errorManager;
        _flashMessage = flashMessage;
        _configuration = configuration;
        _dbContext = dbContext;
    }

    public override void OnActionExecuting(ActionExecutingContext context) {
        base.OnActionExecuting(context);
        ViewData["Menu"] = "Status";
        ViewData["Title"] = "Status";
    }

    [HttpGet]
    public async Task<IActionResult> Index(string search, string source) {
        var status = new SystemStatusViewModel();

        // Database connection
        try {
            await _dbContext.Database.CanConnectAsync();
            status.DatabaseConnected = true;
        } catch {
            status.DatabaseConnected = false;
        }

        // Data counts (only if DB is connected)
        if (status.DatabaseConnected) {
            status.StockCount = await _dbContext.Set<CommonStock>().CountAsync();
            status.DocumentCount = await _dbContext.Set<Document>().CountAsync();
            status.InsiderTransactionCount = await _dbContext.Set<InsiderTransaction>().CountAsync();
            status.CongressionalTradeCount = await _dbContext.Set<CongressionalTrade>().CountAsync();
            status.InstitutionalHoldingCount = await _dbContext.Set<InstitutionalHolding>().CountAsync();
            status.FailToDeliverCount = await _dbContext.Set<FailToDeliver>().CountAsync();
            status.FredObservationCount = await _dbContext.Set<FredObservation>().CountAsync();
        }

        // MCP API key
        status.McpApiKeyConfigured = !string.IsNullOrEmpty(_configuration["McpApiKey"]);

        // Worker statuses based on configuration
        status.Workers = BuildWorkerStatuses();

        // Error summary
        status.TotalErrorCount = await _errorRepository.GetAll().CountAsync();
        status.UnseenErrorCount = await _errorRepository.GetAll().CountAsync(e => !e.Seen);

        // Filtered errors for the table
        var query = _errorRepository.Search(search);
        if (!string.IsNullOrEmpty(source)) {
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
    public async Task<IActionResult> Show(Guid id) {
        var error = await _errorRepository.Get(id);
        if (error == null) {
            _flashMessage.Error("Error not found.");
            return RedirectToAction(nameof(Index));
        }

        return View(error);
    }

    [HttpPost("{id:guid}/MarkAsSeen")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAsSeen(Guid id) {
        var error = await _errorRepository.Get(id);
        if (error == null) {
            _flashMessage.Error("Error not found.");
            return RedirectToAction(nameof(Index));
        }

        await _errorManager.MarkAsSeen(error);

        _flashMessage.Success("Error marked as seen.");
        return RedirectToAction(nameof(Show), new { id });
    }

    [HttpPost("{id:guid}/Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id) {
        var error = await _errorRepository.Get(id);
        if (error == null) {
            _flashMessage.Error("Error not found.");
            return RedirectToAction(nameof(Index));
        }

        await _errorManager.Delete(error);

        _flashMessage.Success("Error deleted.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("DeleteAll")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAll() {
        await _errorManager.DeleteAll();

        _flashMessage.Success("All errors deleted.");
        return RedirectToAction(nameof(Index));
    }

    private List<WorkerStatus> BuildWorkerStatuses() {
        var secContactEmail = _configuration["Sec:ContactEmail"];
        var secConfigured = !string.IsNullOrEmpty(secContactEmail);
        var finraClientId = _configuration["Finra:ClientId"];
        var fredApiKey = _configuration["Fred:ApiKey"];
        var embeddingEnabled = _configuration.GetValue<bool>("Embedding:Enabled");
        var embeddingBaseUrl = _configuration["Embedding:BaseUrl"];
        var embeddingModel = _configuration["Embedding:ModelName"];

        return [
            new WorkerStatus {
                Name = "SEC Filing Scraper",
                Description = "Scrapes 10-K, 10-Q, 8-K filings and Form 3/4 from SEC EDGAR",
                Active = secConfigured,
                Reason = secConfigured
                    ? "SEC contact email configured"
                    : "SEC_CONTACT_EMAIL not set — required by SEC EDGAR fair access policy. Set it in your .env file."
            },
            new WorkerStatus {
                Name = "Document Processor",
                Description = "Parses and chunks SEC filings for full-text search",
                Active = secConfigured,
                Reason = secConfigured
                    ? "SEC contact email configured"
                    : "Depends on SEC Filing Scraper (SEC_CONTACT_EMAIL not set)"
            },
            new WorkerStatus {
                Name = "Holdings Scraper",
                Description = "Imports institutional ownership from SEC 13F-HR quarterly datasets",
                Active = secConfigured,
                Reason = secConfigured
                    ? "SEC contact email configured"
                    : "SEC_CONTACT_EMAIL not set — required by SEC EDGAR fair access policy. Set it in your .env file."
            },
            new WorkerStatus {
                Name = "Congressional Trade Scraper",
                Description = "Syncs House and Senate stock trade disclosures",
                Active = true,
                Reason = "Always active"
            },
            new WorkerStatus {
                Name = "Short Data Scraper",
                Description = "Imports daily short volume, short interest, and fails-to-deliver from SEC and FINRA",
                Active = secConfigured,
                Reason = !secConfigured
                    ? "SEC_CONTACT_EMAIL not set — required by SEC EDGAR fair access policy. Set it in your .env file."
                    : string.IsNullOrEmpty(finraClientId)
                        ? "FTD active, FINRA short volume/interest disabled (Finra:ClientId not configured)"
                        : "SEC and FINRA configured"
            },
            new WorkerStatus {
                Name = "FRED Economic Data Scraper",
                Description = "Imports economic indicators (interest rates, inflation, employment, GDP, etc.) from the Federal Reserve",
                Active = !string.IsNullOrEmpty(fredApiKey),
                Reason = !string.IsNullOrEmpty(fredApiKey)
                    ? "FRED API key configured"
                    : "Fred:ApiKey not set — get a free key at fred.stlouisfed.org/docs/api/api_key.html"
            },
            new WorkerStatus {
                Name = "Embedding Generator",
                Description = "Generates vector embeddings for semantic search over SEC filings",
                Active = embeddingEnabled && !string.IsNullOrEmpty(embeddingBaseUrl),
                Reason = !embeddingEnabled
                    ? "Disabled (Embedding:Enabled = false)"
                    : string.IsNullOrEmpty(embeddingBaseUrl)
                        ? "No embedding server configured (Embedding:BaseUrl)"
                        : $"Model: {embeddingModel ?? "not set"}, Server: {embeddingBaseUrl}"
            },
        ];
    }
}
