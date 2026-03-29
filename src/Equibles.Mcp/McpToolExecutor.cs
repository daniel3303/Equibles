using Microsoft.Extensions.Logging;

namespace Equibles.Mcp;

public static class McpToolExecutor {
    public static async Task<string> Execute(
        Func<Task<string>> action,
        ILogger logger,
        string toolName,
        string context,
        Func<string, string, string, string, Task> reportError,
        string errorMessage = null
    ) {
        try {
            return await action();
        } catch (Exception ex) {
            logger.LogError(ex, "{ToolName} failed — {Context}", toolName, context);
            try { await reportError(toolName, ex.Message, ex.StackTrace, context); } catch { }
            return errorMessage ?? $"An error occurred while executing {toolName}. Please try again.";
        }
    }
}
