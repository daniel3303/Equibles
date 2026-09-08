using System;
using Equibles.Mcp.Helpers;
using Equibles.Mcp.Middleware;
using Microsoft.AspNetCore.Http;

namespace Equibles.UnitTests.Mcp;

// What a caller has to send to get GCF, and what must happen when it sends nothing or
// something unrecognized. The format is observed from inside next, because that is where a
// tool runs, and because after the middleware returns the async machinery has already put
// the caller's context back whatever the scope did. Shares EQUIBLES_OUTPUT_FORMAT with the
// other GCF suites.
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

    // The header only wins when it parses. A stray value there must not veto the URL, or a
    // client with one bad header setting could never ask for a format at all.
    [Fact]
    public async Task InvokeAsync_HeaderIsUnrecognized_TheQueryParameterStillDecides()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[OutputFormatMiddleware.HeaderName] = "json";
        context.Request.QueryString = new QueryString($"?{OutputFormatMiddleware.QueryName}=gcf");

        await Sut().InvokeAsync(context);

        _observed.Should().Be(McpOutputFormat.Gcf);
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

    // There is deliberately no test here that awaits InvokeAsync and then asserts the scope
    // is gone. The async state machine restores the caller's ExecutionContext on return, so
    // such a test passes even against a Dispose that restores nothing, and would claim
    // coverage it does not have. The restore is pinned where it can actually be observed,
    // synchronously, by OutputFormatScopeTests.Nested_Scopes_Restore_The_Enclosing_Value.
}
