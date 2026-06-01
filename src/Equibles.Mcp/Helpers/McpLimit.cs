namespace Equibles.Mcp.Helpers;

public static class McpLimit
{
    // A negative result cap would flow into .Take(...) as a negative SQL LIMIT, which
    // PostgreSQL rejects and surfaces as the internal-error sentinel. Clamping to non-negative
    // makes a non-positive cap yield zero rows and the existing no-results message instead.
    public static int Clamp(int maxResults) => Math.Max(0, maxResults);
}
