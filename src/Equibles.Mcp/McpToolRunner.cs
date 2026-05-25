using Microsoft.Extensions.Logging;

namespace Equibles.Mcp;

public class McpToolRunner
{
    private readonly ILogger _logger;
    private readonly Func<string, string, string, string, Task> _reportError;

    public McpToolRunner(ILogger logger, Func<string, string, string, string, Task> reportError)
    {
        _logger = logger;
        _reportError = reportError;
    }

    public Task<string> Execute(
        Func<Task<string>> action,
        string toolName,
        string context,
        string errorMessage = null
    ) => McpToolExecutor.Execute(action, _logger, toolName, context, _reportError, errorMessage);
}
