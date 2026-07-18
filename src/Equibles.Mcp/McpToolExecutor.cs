using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Equibles.Mcp;

public static class McpToolExecutor
{
    public static DateOnly ParseDateOr(string text, DateOnly fallback) =>
        !string.IsNullOrEmpty(text)
        && DateOnly.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed
        )
            ? parsed
            : fallback;

    public static DateOnly UtcMonthsAgo(int months) =>
        DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-months));

    public static DateOnly UtcYearsAgo(int years) =>
        DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-years));

    public static (DateOnly Start, DateOnly End) ParseDateRange(
        string startText,
        string endText,
        DateOnly defaultStart
    ) =>
        (
            ParseDateOr(startText, defaultStart),
            ParseDateOr(endText, DateOnly.FromDateTime(DateTime.UtcNow))
        );

    public static string StockNotFound(string ticker) => $"Stock '{ticker}' not found.";

    public static string NormalizeTicker(string ticker) => ticker.Trim().ToUpperInvariant();

    public static async Task<string> Execute(
        Func<Task<string>> action,
        ILogger logger,
        string toolName,
        string context,
        Func<string, Exception, string, Task> reportError,
        string errorMessage = null
    )
    {
        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{ToolName} failed — {Context}", toolName, context);
            // A cancellation (host shutdown winding down an in-flight call, an aborted
            // client) is not a fault worth an Errors row — same drop-by-type policy as
            // ErrorReporter. The reporter receives the exception itself so the recorded
            // row carries the flattened inner chain, not a wrapper's message.
            if (ex is not OperationCanceledException)
            {
                try
                {
                    await reportError(toolName, ex, context);
                }
                catch { }
            }
            return errorMessage
                ?? $"An error occurred while executing {toolName}. Please try again.";
        }
    }
}
