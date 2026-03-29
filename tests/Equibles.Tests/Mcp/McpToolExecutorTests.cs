using Equibles.Mcp;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Equibles.Tests.Mcp;

public class McpToolExecutorTests {
    private readonly ILogger _logger;
    private readonly Func<string, string, string, string, Task> _reportError;

    public McpToolExecutorTests() {
        _logger = Substitute.For<ILogger>();
        _reportError = Substitute.For<Func<string, string, string, string, Task>>();
    }

    [Fact]
    public async Task Execute_SuccessfulAction_ReturnsResult() {
        var result = await McpToolExecutor.Execute(
            () => Task.FromResult("success-payload"),
            _logger,
            "TestTool",
            "ticker=AAPL",
            _reportError);

        result.Should().Be("success-payload");
    }

    [Fact]
    public async Task Execute_ActionThrows_ReturnsDefaultErrorMessage() {
        var result = await McpToolExecutor.Execute(
            () => throw new InvalidOperationException("boom"),
            _logger,
            "TestTool",
            "ticker=AAPL",
            _reportError);

        result.Should().Be("An error occurred while executing TestTool. Please try again.");
    }

    [Fact]
    public async Task Execute_ActionThrows_CallsReportError() {
        var exception = new InvalidOperationException("boom");

        await McpToolExecutor.Execute(
            () => throw exception,
            _logger,
            "TestTool",
            "ticker=AAPL",
            _reportError);

        await _reportError.Received(1).Invoke(
            "TestTool",
            "boom",
            Arg.Is<string>(s => s != null),
            "ticker=AAPL");
    }

    [Fact]
    public async Task Execute_ActionThrows_WithCustomErrorMessage_ReturnsCustomMessage() {
        var result = await McpToolExecutor.Execute(
            () => throw new InvalidOperationException("boom"),
            _logger,
            "TestTool",
            "ticker=AAPL",
            _reportError,
            errorMessage: "Something went wrong with your request.");

        result.Should().Be("Something went wrong with your request.");
    }

    [Fact]
    public async Task Execute_ActionThrows_LogsError() {
        var exception = new InvalidOperationException("boom");

        await McpToolExecutor.Execute(
            () => throw exception,
            _logger,
            "TestTool",
            "ticker=AAPL",
            _reportError);

        _logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("TestTool") && o.ToString()!.Contains("ticker=AAPL")),
            exception,
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task Execute_ReportErrorThrows_ExceptionIsSwallowed() {
        _reportError.Invoke(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .ThrowsAsync(new Exception("reporting failed"));

        var result = await McpToolExecutor.Execute(
            () => throw new InvalidOperationException("boom"),
            _logger,
            "TestTool",
            "ticker=AAPL",
            _reportError);

        result.Should().Be("An error occurred while executing TestTool. Please try again.");
    }
}
