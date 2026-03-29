using System.Net;
using Equibles.Mcp.Contracts;
using Equibles.Mcp.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.Tests.Mcp;

public class ApiKeyMiddlewareTests {
    private readonly IApiKeyValidator _validator;
    private readonly DefaultHttpContext _httpContext;
    private bool _nextCalled;
    private readonly ApiKeyMiddleware _sut;

    public ApiKeyMiddlewareTests() {
        _validator = Substitute.For<IApiKeyValidator>();
        _validator.IsEnabled.Returns(true);
        _httpContext = new DefaultHttpContext();
        _httpContext.Response.Body = new MemoryStream();
        _nextCalled = false;

        _sut = new ApiKeyMiddleware(
            _ => { _nextCalled = true; return Task.CompletedTask; },
            Substitute.For<ILogger<ApiKeyMiddleware>>());
    }

    [Fact]
    public async Task InvokeAsync_ValidatorDisabled_CallsNext() {
        _validator.IsEnabled.Returns(false);

        await _sut.InvokeAsync(_httpContext, _validator);

        _nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_ValidBearerToken_CallsNext() {
        _validator.IsValid("test-key").Returns(true);
        _httpContext.Request.Headers.Authorization = "Bearer test-key";

        await _sut.InvokeAsync(_httpContext, _validator);

        _nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_MissingAuthorizationHeader_Returns401() {
        await _sut.InvokeAsync(_httpContext, _validator);

        _httpContext.Response.StatusCode.Should().Be((int)HttpStatusCode.Unauthorized);
        _nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_NoBearerPrefix_Returns401() {
        _httpContext.Request.Headers.Authorization = "Basic abc123";

        await _sut.InvokeAsync(_httpContext, _validator);

        _httpContext.Response.StatusCode.Should().Be((int)HttpStatusCode.Unauthorized);
        _nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_InvalidApiKey_Returns401() {
        _validator.IsValid("bad-key").Returns(false);
        _httpContext.Request.Headers.Authorization = "Bearer bad-key";

        await _sut.InvokeAsync(_httpContext, _validator);

        _httpContext.Response.StatusCode.Should().Be((int)HttpStatusCode.Unauthorized);
        _nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_BearerWithWhitespace_TrimsAndValidates() {
        _validator.IsValid("my-key").Returns(true);
        _httpContext.Request.Headers.Authorization = "Bearer   my-key  ";

        await _sut.InvokeAsync(_httpContext, _validator);

        _nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_EmptyBearerValue_Returns401() {
        _httpContext.Request.Headers.Authorization = "Bearer ";
        _validator.IsValid("").Returns(false);

        await _sut.InvokeAsync(_httpContext, _validator);

        _httpContext.Response.StatusCode.Should().Be((int)HttpStatusCode.Unauthorized);
        _nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_CaseInsensitiveBearerPrefix_Accepted() {
        _validator.IsValid("my-key").Returns(true);
        _httpContext.Request.Headers.Authorization = "bearer my-key";

        await _sut.InvokeAsync(_httpContext, _validator);

        _nextCalled.Should().BeTrue();
    }
}
