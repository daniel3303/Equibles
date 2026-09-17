namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// One filing's positions could not be kept apart under their retained observation identities.
/// The importer skips that filing and keeps going, so this message is the only record of which
/// holder and security need repairing — it must name the conflict, never just describe it.
/// </summary>
public sealed class HoldingObservationConflictException(string message)
    : InvalidOperationException(message);
