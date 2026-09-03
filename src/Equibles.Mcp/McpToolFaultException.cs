namespace Equibles.Mcp;

/// <summary>
/// An unexpected failure inside a tool body, carrying the user-facing message the tool wants
/// the caller to read. Thrown by <see cref="McpToolExecutor.Execute"/> after the fault has been
/// logged and reported, and translated by <see cref="InvalidParamsTranslatingTool"/> into an
/// in-band tool error (<c>isError: true</c>) — returning the message as ordinary tool TEXT
/// made every in-tool failure count as a success, so no tool error was ever visible to usage
/// telemetry and a calling model could not tell a failure from an answer.
/// </summary>
public class McpToolFaultException : Exception
{
    public McpToolFaultException(string message)
        : base(message) { }

    // A tool that raises the fault itself keeps the underlying failure attached, so the recorded
    // Errors row still carries the real cause behind the caller-facing wording.
    public McpToolFaultException(string message, Exception innerException)
        : base(message, innerException) { }
}
