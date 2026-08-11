using System.Globalization;
using Equibles.CommonStocks.Data.Helpers;
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

    public static string NormalizeTicker(string ticker) =>
        TickerNormalizer.NormalizeDashListed(ticker);

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
        catch (OperationCanceledException)
        {
            // A cancellation (host shutdown winding down an in-flight call, an aborted
            // client) is not a fault worth an Errors row — same drop-by-type policy as
            // ErrorReporter. Propagate it so the SDK handles the abort as an abort, but
            // leave a trace: a cancellation storm (client aborting every call, a wedged
            // upstream timing out as TaskCanceledException) must stay visible in logs.
            logger.LogInformation("{ToolName} cancelled — {Context}", toolName, context);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{ToolName} failed — {Context}", toolName, context);
            // The reporter receives the exception itself so the recorded row carries the
            // flattened inner chain, not a wrapper's message.
            try
            {
                await reportError(toolName, ex, context);
            }
            catch { }
            // Surface the fault as a fault: returning the message as ordinary tool text made
            // the call count as a success (ErrorCount never moved) and left the calling model
            // unable to tell a failure from an answer. InvalidParamsTranslatingTool turns
            // this into an in-band isError tool result carrying the same message.
            throw new McpToolFaultException(
                errorMessage ?? $"An error occurred while executing {toolName}. Please try again."
            );
        }
    }
}
