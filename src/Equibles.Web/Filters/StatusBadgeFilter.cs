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

            var warnings = 0;
            if (string.IsNullOrEmpty(_configuration["Sec:ContactEmail"])) warnings++;
            if (string.IsNullOrEmpty(_configuration["McpApiKey"])) warnings++;
            if (string.IsNullOrEmpty(_configuration["Finra:ClientId"])) warnings++;
            var embeddingEnabled = _configuration.GetValue<bool>("Embedding:Enabled");
            if (!embeddingEnabled || string.IsNullOrEmpty(_configuration["Embedding:BaseUrl"])) warnings++;

            controller.ViewData["StatusBadgeCount"] = unseenErrors + warnings;
        }

        await next();
    }
}
