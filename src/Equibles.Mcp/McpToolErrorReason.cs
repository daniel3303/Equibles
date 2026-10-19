namespace Equibles.Mcp;

/// <summary>
/// Bounded classification of why a tool call came back as an error, handed to the host through
/// <c>HttpContext.Items</c> so usage telemetry can tell client mistakes from server faults.
/// Values are fixed tokens; never error messages or argument values.
/// </summary>
public static class McpToolErrorReason
{
    public const string InvalidParams = "invalid_params";
    public const string Timeout = "timeout";
    public const string ToolFault = "tool_fault";
    public const string Cancelled = "cancelled";

    // HttpContext.Items key holding one of the reason values above.
    public const string ReasonItem = "Equibles.Mcp.ToolErrorReason";

    // HttpContext.Items key holding the rejected argument's name, set only when it is a key of
    // the tool's input schema.
    public const string ParamItem = "Equibles.Mcp.ToolErrorParam";

    // A timeout anywhere in the cause chain classifies the failure as a timeout.
    public static bool IsTimeout(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is TimeoutException)
            {
                return true;
            }
        }
        return false;
    }
}
