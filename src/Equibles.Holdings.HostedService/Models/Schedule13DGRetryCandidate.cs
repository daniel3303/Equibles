using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.HostedService.Models;

/// <summary>
/// A stored 13D/13G detail row without holdings whose position now resolves to a tracked
/// security, by its filed CUSIP or by its subject company. <see cref="ParserVersion"/> is set for a
/// pre-XML filing, which is read again from its complete submission.
/// </summary>
public record Schedule13DGRetryCandidate(
    string AccessionNumber,
    FilingType FilingType,
    bool IsAmendment,
    DateOnly FilingDate,
    DateOnly DateOfEvent,
    string FilerCik,
    string IssuerCik,
    string IssuerCusip,
    int? ParserVersion
)
{
    /// <summary>The CUSIP the position would be stored under; set once resolved.</summary>
    public string PositionCusip { get; init; }
}
