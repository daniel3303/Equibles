namespace Equibles.Integrations.Sec.Contracts;

/// <summary>
/// No-op <see cref="ISecRateLimitNotifier"/> used when no publishing
/// implementation is registered (the MCP server, the web app, and tests). Keeps
/// <see cref="SecEdgarClient"/> free of any hard messaging dependency.
/// </summary>
public sealed class NullSecRateLimitNotifier : ISecRateLimitNotifier
{
    public static readonly NullSecRateLimitNotifier Instance = new();

    public Task RateLimited(TimeSpan pause, string url) => Task.CompletedTask;

    public Task Reachable(string url) => Task.CompletedTask;
}
