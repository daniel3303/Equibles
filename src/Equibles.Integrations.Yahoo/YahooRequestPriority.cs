namespace Equibles.Integrations.Yahoo;

/// <summary>
/// Marks the requests made inside a scope as urgent for the client's shared rate limiter. Every
/// lane draws on one budget, so a request for a session nobody has asked for yet goes ahead of
/// re-asks and profile refreshes; a request outside any scope is background.
/// </summary>
public static class YahooRequestPriority
{
    private static readonly AsyncLocal<bool> Urgent = new();

    public static bool IsUrgent => Urgent.Value;

    public static IDisposable UrgentScope(bool urgent = true)
    {
        var previous = Urgent.Value;
        Urgent.Value = urgent;
        return new Scope(previous);
    }

    private sealed class Scope(bool previous) : IDisposable
    {
        public void Dispose() => Urgent.Value = previous;
    }
}
