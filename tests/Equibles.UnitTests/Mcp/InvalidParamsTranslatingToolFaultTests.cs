using Equibles.Mcp;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NSubstitute;

namespace Equibles.UnitTests.Mcp;

/// <summary>
/// The transport half of the tool-fault contract: <see cref="McpToolExecutor"/> throws
/// <see cref="McpToolFaultException"/> after logging and reporting, and the decorator turns it
/// into an in-band <c>isError</c> tool result — the shape the usage-recording middleware's
/// error detection counts. Without this pair, every in-tool failure serialised as a SUCCESS.
/// </summary>
public class InvalidParamsTranslatingToolFaultTests
{
    private sealed class ThrowingTool : McpServerTool
    {
        private readonly Exception _exception;

        public ThrowingTool(Exception exception)
        {
            _exception = exception;
        }

        public override Tool ProtocolTool => new() { Name = "ThrowingTool" };

        public override IReadOnlyList<object> Metadata => [];

        public override ValueTask<CallToolResult> InvokeAsync(
            RequestContext<CallToolRequestParams> request,
            CancellationToken cancellationToken = default
        ) => throw _exception;
    }

    private static InvalidParamsTranslatingTool Wrap(Exception exception) =>
        new(new ThrowingTool(exception), Substitute.For<ILogger<InvalidParamsTranslatingTool>>());

    [Fact]
    public async Task InvokeAsync_ToolFault_ReturnsIsErrorResultWithTheFaultMessage()
    {
        var result = await Wrap(
                new McpToolFaultException(
                    "An error occurred while executing GetMostHeldStocks. Please try again."
                )
            )
            .InvokeAsync(null!);

        result.IsError.Should().BeTrue();
        result
            .Content.OfType<TextContentBlock>()
            .Single()
            .Text.Should()
            .Be("An error occurred while executing GetMostHeldStocks. Please try again.");
    }

    [Fact]
    public async Task InvokeAsync_Cancellation_Propagates()
    {
        var act = () => Wrap(new OperationCanceledException()).InvokeAsync(null!).AsTask();

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
