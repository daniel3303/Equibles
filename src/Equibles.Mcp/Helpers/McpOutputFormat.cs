namespace Equibles.Mcp.Helpers;

// How a tool answer's tables are framed. The cell values are identical either way;
// only the framing differs, so this is a transport concern and never a data one.
public enum McpOutputFormat
{
    // Markdown tables, the default a client gets when it asks for nothing.
    Markdown,

    // Graph Compact Format (https://gcformat.com), used when it is smaller.
    Gcf,
}
