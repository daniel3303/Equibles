namespace Equibles.Mcp;

public class McpToolContext {
    public string ToolName { get; set; }
    public Dictionary<string, object> Arguments { get; set; } = [];
    public IServiceProvider ServiceProvider { get; set; }
}
