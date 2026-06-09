using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Equibles.Mcp;

/// <summary>
/// Decorates a registered <see cref="McpServerTool"/> so that a missing or malformed
/// required argument — which the SDK's binder surfaces as an
/// <see cref="ArgumentException"/> before the tool body ever runs — comes back to the
/// client as a structured invalid-parameters tool result naming the tool, instead of
/// an opaque "an error occurred" message. A client's input mistake is not a server
/// fault, so it is logged at information level rather than error.
/// </summary>
public class InvalidParamsTranslatingTool : McpServerTool
{
    private readonly McpServerTool _inner;
    private readonly ILogger<InvalidParamsTranslatingTool> _logger;

    public InvalidParamsTranslatingTool(
        McpServerTool inner,
        ILogger<InvalidParamsTranslatingTool> logger
    )
    {
        _inner = inner;
        _logger = logger;
    }

    public override Tool ProtocolTool => _inner.ProtocolTool;

    public override IReadOnlyList<object> Metadata => _inner.Metadata;

    public override async ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await _inner.InvokeAsync(request, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            _logger.LogInformation(
                "Rejected call to {ToolName} with invalid arguments: {Reason}",
                ProtocolTool.Name,
                ex.Message
            );
            return new CallToolResult
            {
                IsError = true,
                Content =
                [
                    new TextContentBlock
                    {
                        Text = $"Invalid parameters for {ProtocolTool.Name}: {ex.Message}",
                    },
                ],
            };
        }
    }
}
