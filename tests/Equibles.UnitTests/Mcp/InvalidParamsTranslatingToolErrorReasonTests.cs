using System.Text.Json;
using Equibles.Mcp;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NSubstitute;

namespace Equibles.UnitTests.Mcp;

/// <summary>
/// The decorator hands usage telemetry a bounded reason (and, for invalid params, a schema
/// parameter name) through HttpContext.Items, so a failed call can be told apart by cause.
/// </summary>
public class InvalidParamsTranslatingToolErrorReasonTests
{
    private static readonly JsonElement Schema = JsonDocument
        .Parse("""{"type":"object","properties":{"ticker":{"type":"string"}}}""")
        .RootElement;

    private sealed class StubTool : McpServerTool
    {
        private readonly Func<CallToolResult> _invoke;

        public StubTool(Func<CallToolResult> invoke)
        {
            _invoke = invoke;
        }

        public override Tool ProtocolTool =>
            new() { Name = "SearchDocuments", InputSchema = Schema };

        public override IReadOnlyList<object> Metadata => [];

        public override ValueTask<CallToolResult> InvokeAsync(
            RequestContext<CallToolRequestParams> request,
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult(_invoke());
    }

    private readonly DefaultHttpContext _httpContext = new();

    private InvalidParamsTranslatingTool Wrap(Func<CallToolResult> invoke) =>
        new(
            new StubTool(invoke),
            Substitute.For<ILogger<InvalidParamsTranslatingTool>>(),
            new HttpContextAccessor { HttpContext = _httpContext }
        );

    private object Reason => _httpContext.Items[McpToolErrorReason.ReasonItem];

    private object Param => _httpContext.Items[McpToolErrorReason.ParamItem];

    [Fact]
    public async Task InvokeAsync_ArgumentExceptionNamingASchemaProperty_RecordsInvalidParamsAndParam()
    {
        var result = await Wrap(() => throw new ArgumentException("missing", "ticker"))
            .InvokeAsync(null!);

        result.IsError.Should().BeTrue();
        Reason.Should().Be(McpToolErrorReason.InvalidParams);
        Param.Should().Be("ticker");
    }

    [Fact]
    public async Task InvokeAsync_ArgumentExceptionNamingAnUnknownParam_RecordsReasonWithoutParam()
    {
        await Wrap(() => throw new ArgumentException("missing", "arguments")).InvokeAsync(null!);

        Reason.Should().Be(McpToolErrorReason.InvalidParams);
        _httpContext.Items.ContainsKey(McpToolErrorReason.ParamItem).Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_JsonException_RecordsInvalidParamsWithoutParam()
    {
        await Wrap(() => throw new JsonException("wrong type")).InvokeAsync(null!);

        Reason.Should().Be(McpToolErrorReason.InvalidParams);
        _httpContext.Items.ContainsKey(McpToolErrorReason.ParamItem).Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_InvalidParams_LogsNeitherTheMessageNorArgumentValues()
    {
        var logger = Substitute.For<ILogger<InvalidParamsTranslatingTool>>();
        var tool = new InvalidParamsTranslatingTool(
            new StubTool(() => throw new ArgumentException("secret-value-AAPL", "ticker")),
            logger
        );

        await tool.InvokeAsync(null!);

        logger
            .Received(1)
            .Log(
                LogLevel.Information,
                Arg.Any<EventId>(),
                Arg.Is<object>(o =>
                    !o.ToString()!.Contains("secret-value-AAPL")
                    && o.ToString()!.Contains("ticker")
                    && o.ToString()!.Contains(nameof(ArgumentException))
                ),
                null,
                Arg.Any<Func<object, Exception?, string>>()
            );
    }

    [Fact]
    public async Task InvokeAsync_FaultWithTimeoutReason_RecordsTimeout()
    {
        await Wrap(() => throw new McpToolFaultException("retry", null, McpToolErrorReason.Timeout))
            .InvokeAsync(null!);

        Reason.Should().Be(McpToolErrorReason.Timeout);
    }

    [Fact]
    public async Task InvokeAsync_PlainFault_RecordsToolFault()
    {
        await Wrap(() => throw new McpToolFaultException("An error occurred.")).InvokeAsync(null!);

        Reason.Should().Be(McpToolErrorReason.ToolFault);
    }

    [Fact]
    public async Task InvokeAsync_InnerReturnsIsErrorResult_RecordsToolFault()
    {
        await Wrap(() => new CallToolResult { IsError = true, Content = [] }).InvokeAsync(null!);

        Reason.Should().Be(McpToolErrorReason.ToolFault);
    }

    [Fact]
    public async Task InvokeAsync_InnerReturnsIsErrorAfterReasonWasSet_KeepsTheEarlierReason()
    {
        _httpContext.Items[McpToolErrorReason.ReasonItem] = McpToolErrorReason.Timeout;

        await Wrap(() => new CallToolResult { IsError = true, Content = [] }).InvokeAsync(null!);

        Reason.Should().Be(McpToolErrorReason.Timeout);
    }

    [Fact]
    public async Task InvokeAsync_Success_RecordsNoReason()
    {
        await Wrap(() => new CallToolResult { Content = [] }).InvokeAsync(null!);

        _httpContext.Items.ContainsKey(McpToolErrorReason.ReasonItem).Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_Cancellation_RecordsCancelledAndPropagates()
    {
        var act = () =>
            Wrap(() => throw new OperationCanceledException()).InvokeAsync(null!).AsTask();

        await act.Should().ThrowAsync<OperationCanceledException>();
        Reason.Should().Be(McpToolErrorReason.Cancelled);
    }

    [Fact]
    public async Task InvokeAsync_CancellationCausedByAnUpstreamTimeout_RecordsTimeout()
    {
        var act = () =>
            Wrap(() => throw new TaskCanceledException("upstream", new TimeoutException()))
                .InvokeAsync(null!)
                .AsTask();

        await act.Should().ThrowAsync<OperationCanceledException>();
        Reason.Should().Be(McpToolErrorReason.Timeout);
    }

    [Fact]
    public async Task InvokeAsync_WithoutHttpContextAccessor_StillTranslates()
    {
        var tool = new InvalidParamsTranslatingTool(
            new StubTool(() => throw new ArgumentException("missing", "ticker")),
            Substitute.For<ILogger<InvalidParamsTranslatingTool>>()
        );

        var result = await tool.InvokeAsync(null!);

        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_SdkBinderMissingRequiredArgument_RecordsTheSchemaParam()
    {
        // The real binder reports ParamName "arguments" and names the parameter in its message.
        var tool = new InvalidParamsTranslatingTool(
            McpServerTool.Create(
                (string ticker) => ticker,
                new McpServerToolCreateOptions { Name = "GetStock" }
            ),
            Substitute.For<ILogger<InvalidParamsTranslatingTool>>(),
            new HttpContextAccessor { HttpContext = _httpContext }
        );
        var request = new RequestContext<CallToolRequestParams>(
            Substitute.For<McpServer>(),
            new JsonRpcRequest { Method = RequestMethods.ToolsCall },
            new CallToolRequestParams { Name = "GetStock" }
        );

        var result = await tool.InvokeAsync(request);

        result.IsError.Should().BeTrue();
        Reason.Should().Be(McpToolErrorReason.InvalidParams);
        Param.Should().Be("ticker");
    }

    [Fact]
    public async Task InvokeAsync_MessageQuotingANonSchemaName_RecordsNoParam()
    {
        await Wrap(() => throw new ArgumentException("bad value 'AAPL'", "arguments"))
            .InvokeAsync(null!);

        _httpContext.Items.ContainsKey(McpToolErrorReason.ParamItem).Should().BeFalse();
    }
}
