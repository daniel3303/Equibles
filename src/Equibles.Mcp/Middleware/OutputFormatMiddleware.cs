using Equibles.Mcp.Helpers;
using Microsoft.AspNetCore.Http;

namespace Equibles.Mcp.Middleware;

// Reads the output format one MCP request asked for and carries it, through
// OutputFormatScope, for the rest of that request. Without this a shared server can only
// offer GCF through EQUIBLES_OUTPUT_FORMAT, which is process-wide: on for every caller or
// none of them.
//
// The format is taken from the X-Equibles-Output-Format header, or from an
// ?output_format= query parameter for the clients that can only configure a URL. An
// absent or unrecognized value changes nothing and is never an error, so a typo returns
// the server's usual output rather than a failed tool call.
//
// Each tool call arrives as its own request and is dispatched on that request's own async
// flow, so the scope reaches the tool and no further. A transport that instead ran calls on
// a connection opened earlier would carry the format of whichever request opened it.
public class OutputFormatMiddleware
{
    public const string HeaderName = "X-Equibles-Output-Format";
    public const string QueryName = "output_format";

    private readonly RequestDelegate _next;

    public OutputFormatMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!TryReadRequestedFormat(context.Request, out var format))
        {
            await _next(context);
            return;
        }

        using (OutputFormatScope.Enter(format))
        {
            await _next(context);
        }
    }

    // A recognised header wins over the query parameter, matching how a request states its
    // API key. An unrecognised header is not a veto: it falls through, so a stray value in a
    // client's header config cannot mask the format the URL asks for.
    private static bool TryReadRequestedFormat(HttpRequest request, out McpOutputFormat format)
    {
        if (OutputFormatScope.TryParse(request.Headers[HeaderName].ToString(), out format))
            return true;

        return OutputFormatScope.TryParse(request.Query[QueryName].ToString(), out format);
    }
}
