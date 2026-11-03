namespace Equibles.Congress.HostedService.Services;

/// <summary>
/// Senate eFD refuses this host. <see cref="Observed"/> is true when eFD answered with the denial
/// this cycle, and false when <see cref="SenateAccessGate"/> held the attempt because a recent
/// denial is on record.
/// </summary>
public class SenateAccessDeniedException : Exception
{
    public SenateAccessDeniedException(string message, bool observed)
        : base(message)
    {
        Observed = observed;
    }

    public bool Observed { get; }
}
