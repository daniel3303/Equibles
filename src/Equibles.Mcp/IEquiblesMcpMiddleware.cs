namespace Equibles.Mcp;

public interface IEquiblesMcpMiddleware {
    Task<object> Invoke(McpToolContext context, Func<Task<object>> next);
}
