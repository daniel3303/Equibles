using Equibles.Web.Controllers.Abstract;
using Equibles.Web.Services;
using Equibles.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace Equibles.Web.Controllers;

public class ChangelogController : BaseController
{
    private const string GitHubChangelogUrl =
        "https://github.com/daniel3303/Equibles/blob/main/CHANGELOG.md";

    private readonly ChangelogService _changelogService;

    public ChangelogController(
        ILogger<ChangelogController> logger,
        ChangelogService changelogService
    )
        : base(logger)
    {
        _changelogService = changelogService;
    }

    [HttpGet]
    public IActionResult Index()
    {
        var html = _changelogService.RenderHtml();
        if (html == null)
        {
            // Graceful fallback when the file was not shipped with the build.
            Logger.LogWarning("CHANGELOG.md not found; redirecting to GitHub");
            return Redirect(GitHubChangelogUrl);
        }

        ViewData["Title"] = "Changelog";
        return View(new ChangelogViewModel { Html = html });
    }
}
