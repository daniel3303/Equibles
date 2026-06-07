namespace Equibles.Mcp.Helpers;

public static class McpLimit
{
    // Upper bound on the number of rows any MCP tool will return. An unbounded cap from the
    // client (e.g. int.MaxValue) would flow into .Take(...) and pull/render an enormous result
    // set, exhausting memory and database time.
    public const int MaxResults = 500;

    // Clamps the client-supplied result cap to [0, MaxResults]. A negative cap would flow into
    // .Take(...) as a negative SQL LIMIT, which PostgreSQL rejects and surfaces as the
    // internal-error sentinel; clamping to non-negative makes a non-positive cap yield zero rows
    // and the existing no-results message instead. The upper bound guards against resource
    // exhaustion from an oversized request.
    public static int Clamp(int maxResults) => Math.Clamp(maxResults, 0, MaxResults);
}
