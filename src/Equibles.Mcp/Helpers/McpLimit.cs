namespace Equibles.Mcp.Helpers;

public static class McpLimit
{
    // Upper bound on the number of rows any MCP tool will return. An unbounded cap from the
    // client (e.g. int.MaxValue) would flow into .Take(...) and pull/render an enormous result
    // set, exhausting memory and database time.
    public const int MaxResults = 500;

    // Clamps the client-supplied result cap to [1, MaxResults]. A non-positive cap is nonsensical:
    // a negative value would flow into .Take(...) as a negative SQL LIMIT (PostgreSQL rejects it as
    // an internal error), and a cap of zero yields zero rows, which makes a tool render its factual
    // empty-state message (e.g. "no annual disclosure found") even for a subject that has data — a
    // false claim served to the caller. Flooring at 1 keeps at least one row so a nonsensical limit
    // never turns "has data" into "has no data". No tool defaults maxResults to 0, so the floor only
    // ever affects an explicit non-positive request. The upper bound guards against resource
    // exhaustion from an oversized request.
    public static int Clamp(int maxResults) => Math.Clamp(maxResults, 1, MaxResults);

    // Upper bound on a paging offset. Mirrors the REST host's MaxOffset: a deep offset makes
    // the database skip-scan that many rows, so an unbounded value is a resource-exhaustion
    // vector just like an unbounded cap.
    public const int MaxOffset = 100_000;

    // Clamps a client-supplied paging offset to [0, MaxOffset]. A negative offset would flow
    // into .Skip(...) as a negative SQL OFFSET; zero (the default) means "from the top".
    public static int ClampOffset(int offset) => Math.Clamp(offset, 0, MaxOffset);
}
