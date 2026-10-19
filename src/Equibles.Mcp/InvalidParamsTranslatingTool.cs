using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Equibles.Mcp;

/// <summary>
/// Decorates a registered <see cref="McpServerTool"/> so that a missing or malformed
/// required argument — which the SDK's binder surfaces as an
/// <see cref="ArgumentException"/> (missing argument) or a
/// <see cref="JsonException"/> (argument of the wrong JSON type, e.g. an array where
/// a string is declared) before the tool body ever runs — comes back to the client as
/// a structured invalid-parameters tool result naming the tool, instead of an opaque
/// "an error occurred" message. A client's input mistake is not a server fault, so it
/// is logged at information level rather than error. Every error outcome also records a
/// bounded <see cref="McpToolErrorReason"/> on the HTTP request for usage telemetry.
/// </summary>
public class InvalidParamsTranslatingTool : McpServerTool
{
    private readonly McpServerTool _inner;
    private readonly ILogger<InvalidParamsTranslatingTool> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public InvalidParamsTranslatingTool(
        McpServerTool inner,
        ILogger<InvalidParamsTranslatingTool> logger,
        IHttpContextAccessor httpContextAccessor = null
    )
    {
        _inner = inner;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
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
            var result = await _inner.InvokeAsync(request, cancellationToken);
            if (result?.IsError == true)
            {
                RecordReason(McpToolErrorReason.ToolFault, overwrite: false);
            }
            return result;
        }
        catch (McpToolFaultException ex)
        {
            // Already logged and reported by McpToolExecutor — this is only the transport
            // translation: an in-band tool error the client (and the usage-recording
            // middleware's isError detection) can see, instead of a success-shaped string.
            RecordReason(ex.Reason);
            return new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = ex.Message }],
            };
        }
        catch (OperationCanceledException ex)
        {
            // An upstream client timeout also surfaces as a cancellation; only a caller abort is "cancelled".
            RecordReason(
                !cancellationToken.IsCancellationRequested && McpToolErrorReason.IsTimeout(ex)
                    ? McpToolErrorReason.Timeout
                    : McpToolErrorReason.Cancelled
            );
            throw;
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException)
        {
            var paramName = SchemaParamName(ex as ArgumentException);
            RecordReason(McpToolErrorReason.InvalidParams);
            if (paramName != null)
            {
                RecordItem(McpToolErrorReason.ParamItem, paramName, overwrite: true);
            }
            // The message can echo argument values, so the log keeps only bounded fields.
            _logger.LogInformation(
                "Rejected call to {ToolName} with invalid arguments: {ParamName} {ExceptionType}",
                ProtocolTool.Name,
                paramName,
                ex.GetType().Name
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

    // The rejected parameter, kept only when it is a declared schema property so the telemetry
    // value stays within the tool's own vocabulary. The SDK binder reports a missing argument
    // with ParamName "arguments" and the parameter quoted in its message, so both are checked.
    private string SchemaParamName(ArgumentException exception)
    {
        var schema = ProtocolTool.InputSchema;
        if (
            exception == null
            || schema.ValueKind != JsonValueKind.Object
            || !schema.TryGetProperty("properties", out var properties)
            || properties.ValueKind != JsonValueKind.Object
        )
        {
            return null;
        }
        if (
            !string.IsNullOrEmpty(exception.ParamName)
            && properties.TryGetProperty(exception.ParamName, out _)
        )
        {
            return exception.ParamName;
        }
        string quoted = null;
        foreach (var property in properties.EnumerateObject())
        {
            if (exception.Message.Contains($"'{property.Name}'", StringComparison.Ordinal))
            {
                if (quoted != null)
                {
                    return null;
                }
                quoted = property.Name;
            }
        }
        return quoted;
    }

    private void RecordReason(string reason, bool overwrite = true) =>
        RecordItem(McpToolErrorReason.ReasonItem, reason, overwrite);

    private void RecordItem(string key, string value, bool overwrite)
    {
        var items = _httpContextAccessor?.HttpContext?.Items;
        if (items == null || (!overwrite && items.ContainsKey(key)))
        {
            return;
        }
        items[key] = value;
    }
}
