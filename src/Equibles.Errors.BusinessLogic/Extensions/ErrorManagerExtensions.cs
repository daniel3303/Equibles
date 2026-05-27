using Equibles.Errors.Data.Models;

namespace Equibles.Errors.BusinessLogic.Extensions;

public static class ErrorManagerExtensions
{
    public static Func<string, string, string, string, Task> AsMcpErrorReporter(
        this ErrorManager errorManager
    ) => (tool, msg, stack, ctx) => errorManager.Create(ErrorSource.McpTool, tool, msg, stack, ctx);
}
