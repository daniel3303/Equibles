using Equibles.Core.AutoWiring;
using Equibles.Errors.Data.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Equibles.Errors.BusinessLogic;

[Service(ServiceLifetime.Singleton)]
public class ErrorReporter {
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ErrorReporter> _logger;

    public ErrorReporter(IServiceScopeFactory scopeFactory, ILogger<ErrorReporter> logger) {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task Report(ErrorSource source, string context, string message, string stackTrace,
        string requestSummary = null) {
        try {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var errorManager = scope.ServiceProvider.GetRequiredService<ErrorManager>();
            await errorManager.Create(source, context, message, stackTrace, requestSummary);
        } catch (Exception ex) {
            _logger.LogDebug(ex, "Failed to report error for {Context}", context);
        }
    }
}
