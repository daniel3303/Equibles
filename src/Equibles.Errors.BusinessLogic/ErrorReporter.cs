using Equibles.Core.AutoWiring;
using Equibles.Errors.Data.Models;
using Equibles.Messaging.Contracts.Activity;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Equibles.Errors.BusinessLogic;

[Service(ServiceLifetime.Singleton)]
public class ErrorReporter
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ErrorReporter> _logger;

    public ErrorReporter(IServiceScopeFactory scopeFactory, ILogger<ErrorReporter> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task Report(
        ErrorSource source,
        string context,
        string message,
        string stackTrace,
        string requestSummary = null
    )
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var errorManager = scope.ServiceProvider.GetRequiredService<ErrorManager>();
            await errorManager.Create(source, context, message, stackTrace, requestSummary);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to report error for {Context}", context);
        }

        // Surface the error on the live activity feed so it shows up alongside
        // normal scraper lifecycle events. Best-effort — a missing bus
        // (tests, hosts without messaging) silently no-ops.
        await PublishActivity(source, context, message);
    }

    private async Task PublishActivity(ErrorSource source, string context, string message)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var bus = scope.ServiceProvider.GetService<IBus>();
            if (bus is null)
                return;

            await bus.Publish(
                new ScraperActivity(
                    Source: source.Value,
                    Severity: ScraperActivitySeverity.Error,
                    Message: $"{context}: {message}",
                    Timestamp: DateTimeOffset.UtcNow,
                    CorrelationId: Guid.NewGuid().ToString()
                )
            );
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to publish ScraperActivity for {Context}", context);
        }
    }
}
