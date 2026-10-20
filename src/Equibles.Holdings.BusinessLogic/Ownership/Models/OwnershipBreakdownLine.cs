namespace Equibles.Holdings.BusinessLogic.Ownership.Models;

/// <summary>One bucket of the institutional (13F) shares split by holder type, style or location.</summary>
public sealed record OwnershipBreakdownLine(
    string Token,
    string Label,
    int Order,
    long Shares,
    int Holders,
    decimal? PercentOutstanding
);
