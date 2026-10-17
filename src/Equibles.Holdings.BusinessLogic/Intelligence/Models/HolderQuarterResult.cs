using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.BusinessLogic.Intelligence.Models;

/// <summary>
/// A filer's metrics row for one quarter, with the securities whose presence bit for the quarter
/// must be set (held, bit missing) or cleared (bit set, no longer held).
/// </summary>
public sealed record HolderQuarterResult(
    HolderQuarterlyMetrics Metrics,
    IReadOnlyList<HolderSecurityKey> PresenceToSet,
    IReadOnlyList<HolderSecurityKey> PresenceToClear
);
