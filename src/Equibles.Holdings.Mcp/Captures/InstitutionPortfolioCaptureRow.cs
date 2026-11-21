using Equibles.Holdings.BusinessLogic.Intelligence.Models;
using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.Mcp.Captures;

/// <summary>
/// One holding row as the answer prints it. <see cref="Shares"/> and
/// <see cref="EstimatedCostPerShare"/> are on today's split basis; principal rows keep their
/// filed amount.
/// </summary>
public sealed record InstitutionPortfolioCaptureRow(
    Guid HoldingId,
    Guid IssuerId,
    string ListedTicker,
    string PrimaryTicker,
    string CompanyName,
    OptionType? OptionType,
    ShareType ShareType,
    long Shares,
    long Value,
    double PercentOfPortfolio,
    int? HeldQuarters,
    bool HeldSinceCoverageStart,
    HolderCostStatus? CostStatus,
    decimal? EstimatedCostPerShare
);
