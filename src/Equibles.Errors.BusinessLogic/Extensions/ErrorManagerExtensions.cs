using Equibles.Errors.Data.Models;

namespace Equibles.Errors.BusinessLogic.Extensions;

public static class ErrorManagerExtensions
{
    // Takes the exception itself so the recorded Message carries the flattened
    // inner-exception chain (ToSummaryMessage) and the StackTrace column the full
    // ToString() — a raw ex.Message left wrapper rows ("likely due to a transient
    // failure") undiagnosable on the Errors page.
    public static Func<string, Exception, string, Task> AsMcpErrorReporter(
        this ErrorManager errorManager
    ) =>
        (tool, ex, ctx) =>
            errorManager.Create(
                ErrorSource.McpTool,
                tool,
                ex.ToSummaryMessage(),
                ex.ToString(),
                ctx
            );
}
