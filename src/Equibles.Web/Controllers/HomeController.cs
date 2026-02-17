using Equibles.Web.Controllers.Abstract;
using Microsoft.AspNetCore.Mvc;

namespace Equibles.Web.Controllers;

public class HomeController : BaseController {
    public HomeController(ILogger<HomeController> logger) : base(logger) {
    }

    [HttpGet("/Home/Error/{statusCode?}")]
    public IActionResult Error(int? statusCode = null) {
        var code = statusCode ?? 500;
        Response.StatusCode = code;
        ViewData["Title"] = code switch {
            404 => "Page Not Found",
            429 => "Too Many Requests",
            _ => "Something Went Wrong"
        };
        ViewData["Description"] = code switch {
            404 => "The page you're looking for doesn't exist or has been moved.",
            429 => "You've made too many requests. Please wait a moment and try again.",
            _ => "An unexpected error occurred. Please try again later."
        };
        return View();
    }

    [HttpGet("/")]
    public IActionResult Index() {
        ViewData["Title"] = "Equibles — Open-Source Financial Data Platform";
        return View();
    }
}
