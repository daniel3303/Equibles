namespace Equibles.Worker;

/// <summary>
/// Thrown by <see cref="OutboundHostGate.WaitForTurn"/> when the target host is parked in a
/// rate-limit cooldown. Callers treat it as a transient skip — not a definitive failure — so the
/// work item is retried after the cooldown rather than written off.
/// </summary>
public sealed class HostCoolingDownException : Exception
{
    public HostCoolingDownException(string host, DateTimeOffset until)
        : base($"Host '{host}' is in a rate-limit cooldown until {until:u}.")
    {
        Host = host;
        Until = until;
    }

    public string Host { get; }
    public DateTimeOffset Until { get; }
}
