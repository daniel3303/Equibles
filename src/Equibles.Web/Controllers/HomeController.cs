using Equibles.Web.Controllers.Abstract;
using Microsoft.AspNetCore.Mvc;

namespace Equibles.Web.Controllers;

public class HomeController : BaseController
{
    private readonly IConfiguration _configuration;

    public HomeController(ILogger<HomeController> logger, IConfiguration configuration)
        : base(logger)
    {
        _configuration = configuration;
    }

    [HttpGet("/home/error/{statusCode?}")]
    public IActionResult Error(int? statusCode = null)
    {
        var code = statusCode ?? 500;
        // The route value is echoed onto Response.StatusCode; an out-of-range
        // value (e.g. 0) makes Kestrel emit a malformed status line. HTTP status
        // codes are constrained to 100-599 (RFC 9110) — fall back to 500.
        if (code < 100 || code > 599)
            code = 500;
        Response.StatusCode = code;
        ViewData["Title"] = code switch
        {
            404 => "Page Not Found",
            429 => "Too Many Requests",
            _ => "Something Went Wrong",
        };
        ViewData["Description"] = code switch
        {
            404 => "The page you're looking for doesn't exist or has been moved.",
            429 => "You've made too many requests. Please wait a moment and try again.",
            _ => "An unexpected error occurred. Please try again later.",
        };
        return View();
    }

    [HttpGet("/")]
    public IActionResult Index()
    {
        ViewData["Title"] = "Equibles — Open-Source Financial Data Platform";
        return View();
    }

    [HttpGet]
    public IActionResult Connect()
    {
        ViewData["Title"] = "Connect AI Assistant";

        var mcpPort = _configuration["McpPort"] ?? "8081";
        var scheme = Request.Scheme;
        var host = Request.Host.Host;
        var mcpUrl = $"{scheme}://{host}:{mcpPort}/mcp";
        var apiKey = _configuration["McpApiKey"] ?? "";

        ViewData["McpUrl"] = mcpUrl;
        ViewData["ApiKey"] = apiKey;

        return View();
    }
}
