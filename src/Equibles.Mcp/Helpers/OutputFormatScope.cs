using System;
using System.Threading;

namespace Equibles.Mcp.Helpers;

// The output format one request asked for, carried ambiently for the duration of that
// request. MarkdownTable and GcfTable are static helpers reached from every tool, so
// there is no service to inject a per-request choice into; an AsyncLocal set by the host
// before it dispatches the call flows into the tool and back out again.
//
// A host serving many callers needs this: EQUIBLES_OUTPUT_FORMAT is process-wide, so on a
// shared server it can only be on for everyone or no one. The scope makes GCF something a
// single client opts into. It holds because each tool call arrives as its own request and
// the SDK dispatches it on that request's own async flow, in both stateless and session
// mode; a transport that ran calls on a connection opened earlier would not carry it.
public static class OutputFormatScope
{
    private static readonly AsyncLocal<McpOutputFormat?> Ambient = new();

    // The format this request asked for, or null when it asked for nothing and the
    // process-wide default applies.
    public static McpOutputFormat? Current => Ambient.Value;

    // Enters the scope for the current async flow; dispose restores the previous value,
    // so a nested scope cannot leak past its own block.
    public static IDisposable Enter(McpOutputFormat format)
    {
        var previous = Ambient.Value;
        Ambient.Value = format;
        return new Restore(previous);
    }

    // Reads a client-supplied format name. An unrecognized value is NOT an error and NOT
    // a request for markdown: it returns false so the caller leaves the process default
    // in place, because a typo must never quietly change what a tool returns.
    public static bool TryParse(string value, out McpOutputFormat format)
    {
        format = McpOutputFormat.Markdown;

        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return false;

        if (string.Equals(trimmed, "gcf", StringComparison.OrdinalIgnoreCase))
        {
            format = McpOutputFormat.Gcf;
            return true;
        }

        if (
            string.Equals(trimmed, "markdown", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "md", StringComparison.OrdinalIgnoreCase)
        )
        {
            format = McpOutputFormat.Markdown;
            return true;
        }

        return false;
    }

    private sealed class Restore : IDisposable
    {
        private readonly McpOutputFormat? _previous;

        public Restore(McpOutputFormat? previous) => _previous = previous;

        public void Dispose() => Ambient.Value = _previous;
    }
}
