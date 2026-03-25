using System.Net;
using System.Text.Json;
using Equibles.Mcp.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Equibles.Mcp.Middleware;

public class ApiKeyMiddleware {
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyMiddleware> _logger;

    public ApiKeyMiddleware(RequestDelegate next, ILogger<ApiKeyMiddleware> logger) {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IApiKeyValidator validator) {
        if (!validator.IsEnabled) {
            await _next(context);
            return;
        }

        var authHeader = context.Request.Headers.Authorization.ToString();

        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) {
            _logger.LogWarning("MCP request rejected: missing or malformed Authorization header");
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = "API key required. Use Authorization: Bearer <key>" }));
            return;
        }

        var apiKey = authHeader["Bearer ".Length..].Trim();

        if (!await validator.IsValid(apiKey)) {
            _logger.LogWarning("MCP request rejected: invalid API key");
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = "Invalid API key" }));
            return;
        }

        await _next(context);
    }
}
