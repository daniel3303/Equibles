using System;
using Equibles.Mcp.Helpers;
using Equibles.Mcp.Middleware;
using Microsoft.AspNetCore.Http;

namespace Equibles.UnitTests.Mcp;

// What a caller has to send to get GCF, and what must happen when it sends nothing or
// something unrecognized. The format is observed from inside next, because that is where
// a tool runs; asserting it after the middleware returns would only prove the scope was
// disposed. Shares EQUIBLES_OUTPUT_FORMAT with the other GCF suites.
[Collection("EquiblesOutputFormatEnv")]
public class OutputFormatMiddlewareTests : IDisposable
{
    private McpOutputFormat? _observed;
    private bool _nextCalled;

    public OutputFormatMiddlewareTests() =>
        Environment.SetEnvironmentVariable("EQUIBLES_OUTPUT_FORMAT", null);

    public void Dispose() => Environment.SetEnvironmentVariable("EQUIBLES_OUTPUT_FORMAT", null);

    private OutputFormatMiddleware Sut() =>
        new(_ =>
        {
            _nextCalled = true;
            _observed = OutputFormatScope.Current;
            return Task.CompletedTask;
        });

    [Fact]
    public async Task InvokeAsync_HeaderAsksForGcf_TheToolSeesGcf()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[OutputFormatMiddleware.HeaderName] = "gcf";

        await Sut().InvokeAsync(context);

        _observed.Should().Be(McpOutputFormat.Gcf);
    }

    // The query parameter is what a client whose configuration is only a URL can use.
    [Fact]
    public async Task InvokeAsync_QueryParameterAsksForGcf_TheToolSeesGcf()
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?output_format=gcf");

        await Sut().InvokeAsync(context);

        _observed.Should().Be(McpOutputFormat.Gcf);
    }

    [Fact]
    public async Task InvokeAsync_HeaderAndQueryDisagree_TheHeaderWins()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[OutputFormatMiddleware.HeaderName] = "markdown";
        context.Request.QueryString = new QueryString("?output_format=gcf");

        await Sut().InvokeAsync(context);

        _observed.Should().Be(McpOutputFormat.Markdown);
    }

    [Fact]
    public async Task InvokeAsync_NoFormatRequested_LeavesTheProcessDefaultInPlace()
    {
        var context = new DefaultHttpContext();

        await Sut().InvokeAsync(context);

        _nextCalled.Should().BeTrue();
        _observed.Should().BeNull("an unscoped request must fall through to the server's default");
    }

    // A typo must not be an error and must not silently pick markdown, which on a
    // GCF-default server would change every answer that request returns.
    [Fact]
    public async Task InvokeAsync_UnrecognizedFormat_IsIgnoredRatherThanRefused()
    {
        Environment.SetEnvironmentVariable("EQUIBLES_OUTPUT_FORMAT", "gcf");
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?output_format=gzf");

        var sut = new OutputFormatMiddleware(_ =>
        {
            _nextCalled = true;
            _observed = OutputFormatScope.Current;
            GcfTable.Enabled.Should().BeTrue("the server default still decides");
            return Task.CompletedTask;
        });

        await sut.InvokeAsync(context);

        _nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        _observed.Should().BeNull();
    }

    // One caller's preference must not survive into the next request handled by the same
    // thread, which is the whole risk of an ambient value.
    [Fact]
    public async Task InvokeAsync_Returns_TheScopeIsGone()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[OutputFormatMiddleware.HeaderName] = "gcf";

        await Sut().InvokeAsync(context);

        OutputFormatScope.Current.Should().BeNull();
        GcfTable.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_NextThrows_TheScopeIsStillRestored()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[OutputFormatMiddleware.HeaderName] = "gcf";

        var sut = new OutputFormatMiddleware(_ =>
            throw new InvalidOperationException("tool blew up")
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.InvokeAsync(context));

        OutputFormatScope.Current.Should().BeNull();
    }
}
