using Equibles.Web.Models;
using Equibles.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Equibles.Web.Filters;

/// <summary>
/// Exposes the cached update-check result to every view via
/// <c>ViewData["VersionUpdate"]</c> so the layout can render the banner.
/// Mirrors <see cref="StatusBadgeFilter"/>.
/// </summary>
public class VersionCheckFilter : IAsyncActionFilter
{
    private readonly VersionCheckService _versionCheckService;

    public VersionCheckFilter(VersionCheckService versionCheckService)
    {
        _versionCheckService = versionCheckService;
    }

    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next
    )
    {
        if (context.Controller is Controller controller)
        {
            var result = _versionCheckService.Get();
            if (result.UpdateAvailable)
            {
                controller.ViewData["VersionUpdate"] = result;
            }
        }

        await next();
    }
}
