using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.Mcp.Captures;

/// <summary>
/// One listed holding row. <see cref="Carried"/> marks a combined view's prior-quarter row and
/// <see cref="Withheld"/> one whose filer reported the newest quarter under confidential treatment.
/// </summary>
public sealed record TopHoldersCaptureRow(
    Guid HoldingId,
    Guid HolderId,
    string Name,
    string Cik,
    long Shares,
    long Value,
    double PercentOfTotal,
    DateOnly ReportDate,
    string ListedTicker,
    OptionType? OptionType,
    ShareType ShareType,
    HolderEntityType? EntityType,
    HolderStyleBand? StyleBand,
    bool Carried,
    bool Withheld
);
