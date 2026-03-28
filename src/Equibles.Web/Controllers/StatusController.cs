using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Errors.Repositories;
using Equibles.Web.Controllers.Abstract;
using Equibles.Web.FlashMessage.Contracts;
using Equibles.Web.Models;
using Equibles.Web.Services;
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
    private readonly DataCountService _dataCountService;

    public StatusController(
        ErrorRepository errorRepository,
        ErrorManager errorManager,
        IFlashMessage flashMessage,
        IConfiguration configuration,
        EquiblesDbContext dbContext,
        DataCountService dataCountService,
        ILogger<StatusController> logger
    ) : base(logger) {
        _errorRepository = errorRepository;
        _errorManager = errorManager;
        _flashMessage = flashMessage;
        _configuration = configuration;
        _dbContext = dbContext;
        _dataCountService = dataCountService;
    }

    public override void OnActionExecuting(ActionExecutingContext context) {
        base.OnActionExecuting(context);
        ViewData["Menu"] = "Status";
        ViewData["Title"] = "Status";
    }

    [HttpGet]
    public async Task<IActionResult> Index(string search, string source) {
        var status = await BuildStatus();

        // MCP API key
        status.McpApiKeyConfigured = !string.IsNullOrEmpty(_configuration["McpApiKey"]);

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

    [HttpGet]
    public async Task<IActionResult> Data() {
        var status = await BuildStatus();
        var workers = status.Workers;

        return Json(new {
            status.DatabaseConnected,
            DataCounts = new Dictionary<string, int> {
                ["StockCount"] = status.StockCount,
                ["DocumentCount"] = status.DocumentCount,
                ["InsiderTransactionCount"] = status.InsiderTransactionCount,
                ["CongressionalTradeCount"] = status.CongressionalTradeCount,
                ["InstitutionalHoldingCount"] = status.InstitutionalHoldingCount,
                ["FailToDeliverCount"] = status.FailToDeliverCount,
                ["FredObservationCount"] = status.FredObservationCount
            },
            Workers = workers.Select(w => new { w.Name, w.Active, w.Reason }),
            status.TotalErrorCount,
            status.UnseenErrorCount,
            ActiveWorkerCount = workers.Count(w => w.Active),
            TotalWorkerCount = workers.Count
        });
    }

    [HttpPost("DeleteAll")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAll() {
        await _errorManager.DeleteAll();

        _flashMessage.Success("All errors deleted.");
        return RedirectToAction(nameof(Index));
    }

    private async Task<SystemStatusViewModel> BuildStatus() {
        var status = new SystemStatusViewModel();

        try {
            await _dbContext.Database.CanConnectAsync();
            status.DatabaseConnected = true;
        } catch {
            status.DatabaseConnected = false;
        }

        if (status.DatabaseConnected) {
            status.StockCount = await _dataCountService.GetStockCount();
            status.DocumentCount = await _dataCountService.GetDocumentCount();
            status.InsiderTransactionCount = await _dataCountService.GetInsiderTransactionCount();
            status.CongressionalTradeCount = await _dataCountService.GetCongressionalTradeCount();
            status.InstitutionalHoldingCount = await _dataCountService.GetInstitutionalHoldingCount();
            status.FailToDeliverCount = await _dataCountService.GetFailToDeliverCount();
            status.FredObservationCount = await _dataCountService.GetFredObservationCount();
        }

        status.Workers = BuildWorkerStatuses();
        status.TotalErrorCount = await _errorRepository.GetAll().CountAsync();
        status.UnseenErrorCount = await _errorRepository.GetAll().CountAsync(e => !e.Seen);

        return status;
    }

    private List<WorkerStatus> BuildWorkerStatuses() {
        var secContactEmail = _configuration["Sec:ContactEmail"];
        var secConfigured = !string.IsNullOrEmpty(secContactEmail);
        var finraClientId = _configuration["Finra:ClientId"];
        var finraConfigured = !string.IsNullOrEmpty(finraClientId);
        var fredApiKey = _configuration["Fred:ApiKey"];
        var fredConfigured = !string.IsNullOrEmpty(fredApiKey);
        var embeddingEnabled = _configuration.GetValue<bool>("Embedding:Enabled");
        var embeddingBaseUrl = _configuration["Embedding:BaseUrl"];
        var embeddingModel = _configuration["Embedding:ModelName"];

        const string secMissing = "SEC_CONTACT_EMAIL not set — required by SEC EDGAR fair access policy";

        return [
            // SEC workers
            new WorkerStatus {
                Name = "SEC Filing Scraper",
                Description = "Scrapes 10-K, 10-Q, 8-K filings and Form 3/4 from SEC EDGAR",
                Active = secConfigured,
                Reason = secConfigured ? "SEC contact email configured" : secMissing
            },
            new WorkerStatus {
                Name = "Document Processor",
                Description = "Parses and chunks SEC filings for full-text search",
                Active = secConfigured,
                Reason = secConfigured ? "SEC contact email configured" : secMissing
            },
            new WorkerStatus {
                Name = "Holdings Scraper",
                Description = "Imports institutional ownership from SEC 13F-HR quarterly datasets",
                Active = secConfigured,
                Reason = secConfigured ? "SEC contact email configured" : secMissing
            },
            new WorkerStatus {
                Name = "Fails-to-Deliver Scraper",
                Description = "Imports FTD data from SEC EDGAR",
                Active = secConfigured,
                Reason = secConfigured ? "SEC contact email configured" : secMissing
            },

            // FINRA worker
            new WorkerStatus {
                Name = "FINRA Scraper",
                Description = "Imports daily short volume and short interest from FINRA",
                Active = finraConfigured,
                Reason = finraConfigured
                    ? "FINRA API credentials configured"
                    : "Finra:ClientId not set — get a free key at developer.finra.org"
            },

            // Other workers
            new WorkerStatus {
                Name = "Congressional Trade Scraper",
                Description = "Syncs House and Senate stock trade disclosures",
                Active = true,
                Reason = "Always active"
            },
            new WorkerStatus {
                Name = "FRED Economic Data Scraper",
                Description = "Imports economic indicators from the Federal Reserve (interest rates, inflation, employment, GDP)",
                Active = fredConfigured,
                Reason = fredConfigured
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
