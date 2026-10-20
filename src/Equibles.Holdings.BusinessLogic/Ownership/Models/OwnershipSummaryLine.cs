namespace Equibles.Holdings.BusinessLogic.Ownership.Models;

/// <summary>One line of the ownership summary: its restated shares, holders and share of the issuer.</summary>
public sealed record OwnershipSummaryLine(
    string Label,
    long Shares,
    int Holders,
    decimal? PercentOutstanding,
    bool Summed
);
