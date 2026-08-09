using Equibles.Mcp;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Equibles.UnitTests.Mcp;

public class McpToolExecutorTests
{
    private readonly ILogger _logger;
    private readonly Func<string, Exception, string, Task> _reportError;

    public McpToolExecutorTests()
    {
        _logger = Substitute.For<ILogger>();
        _reportError = Substitute.For<Func<string, Exception, string, Task>>();
    }

    [Fact]
    public async Task Execute_SuccessfulAction_ReturnsResult()
    {
        var result = await McpToolExecutor.Execute(
            () => Task.FromResult("success-payload"),
            _logger,
            "TestTool",
            "ticker=AAPL",
            _reportError
        );

        result.Should().Be("success-payload");
    }

    [Fact]
    public async Task Execute_ActionThrows_ThrowsFaultWithDefaultMessage()
    {
        var act = () =>
            McpToolExecutor.Execute(
                () => throw new InvalidOperationException("boom"),
                _logger,
                "TestTool",
                "ticker=AAPL",
                _reportError
            );

        (await act.Should().ThrowAsync<McpToolFaultException>()).WithMessage(
            "An error occurred while executing TestTool. Please try again."
        );
    }

    [Fact]
    public async Task Execute_ActionThrows_CallsReportError()
    {
        var exception = new InvalidOperationException("boom");

        var act = () =>
            McpToolExecutor.Execute(
                () => throw exception,
                _logger,
                "TestTool",
                "ticker=AAPL",
                _reportError
            );

        await act.Should().ThrowAsync<McpToolFaultException>();
        await _reportError.Received(1).Invoke("TestTool", exception, "ticker=AAPL");
    }

    [Fact]
    public async Task Execute_ActionThrows_WithCustomErrorMessage_ThrowsFaultWithCustomMessage()
    {
        var act = () =>
            McpToolExecutor.Execute(
                () => throw new InvalidOperationException("boom"),
                _logger,
                "TestTool",
                "ticker=AAPL",
                _reportError,
                errorMessage: "Something went wrong with your request."
            );

        (await act.Should().ThrowAsync<McpToolFaultException>()).WithMessage(
            "Something went wrong with your request."
        );
    }

    [Fact]
    public async Task Execute_ActionThrows_LogsError()
    {
        var exception = new InvalidOperationException("boom");

        var act = () =>
            McpToolExecutor.Execute(
                () => throw exception,
                _logger,
                "TestTool",
                "ticker=AAPL",
                _reportError
            );
        await act.Should().ThrowAsync<McpToolFaultException>();

        _logger
            .Received(1)
            .Log(
                LogLevel.Error,
                Arg.Any<EventId>(),
                Arg.Is<object>(o =>
                    o.ToString()!.Contains("TestTool") && o.ToString()!.Contains("ticker=AAPL")
                ),
                exception,
                Arg.Any<Func<object, Exception?, string>>()
            );
    }

    [Fact]
    public async Task Execute_ReportErrorThrows_ExceptionIsSwallowed()
    {
        _reportError
            .Invoke(Arg.Any<string>(), Arg.Any<Exception>(), Arg.Any<string>())
            .ThrowsAsync(new Exception("reporting failed"));

        var act = () =>
            McpToolExecutor.Execute(
                () => throw new InvalidOperationException("boom"),
                _logger,
                "TestTool",
                "ticker=AAPL",
                _reportError
            );

        // The reporter's own failure never masks the tool fault the caller must see.
        (await act.Should().ThrowAsync<McpToolFaultException>()).WithMessage(
            "An error occurred while executing TestTool. Please try again."
        );
    }

    [Fact]
    public async Task Execute_ActionThrowsCancellation_PropagatesWithoutReporting()
    {
        var act = () =>
            McpToolExecutor.Execute(
                () => throw new OperationCanceledException("call aborted"),
                _logger,
                "TestTool",
                "ticker=AAPL",
                _reportError
            );

        // An abort stays an abort — no Errors row, no fault translation: cancellations are noise.
        await act.Should().ThrowAsync<OperationCanceledException>();
        await _reportError
            .DidNotReceive()
            .Invoke(Arg.Any<string>(), Arg.Any<Exception>(), Arg.Any<string>());
    }
}
