using Equibles.Errors.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Web.Filters;

public class StatusBadgeFilter : IAsyncActionFilter {
    private readonly ErrorRepository _errorRepository;
    private readonly IConfiguration _configuration;

    public StatusBadgeFilter(
        ErrorRepository errorRepository,
        IConfiguration configuration
    ) {
        _errorRepository = errorRepository;
        _configuration = configuration;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next) {
        if (context.Controller is Controller controller) {
            var unseenErrors = await _errorRepository.GetAll().CountAsync(e => !e.Seen);

            var inactiveWorkers = 0;
            if (string.IsNullOrEmpty(_configuration["Finra:ClientId"])) inactiveWorkers++;
            var embeddingEnabled = _configuration.GetValue<bool>("Embedding:Enabled");
            if (!embeddingEnabled || string.IsNullOrEmpty(_configuration["Embedding:BaseUrl"])) inactiveWorkers++;

            controller.ViewData["StatusBadgeCount"] = unseenErrors + inactiveWorkers;
        }

        await next();
    }
}
