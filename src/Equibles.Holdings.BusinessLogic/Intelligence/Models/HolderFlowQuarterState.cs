namespace Equibles.Holdings.BusinessLogic.Intelligence.Models;

/// <summary>A computed intelligence quarter: when, and by which calculation version.</summary>
public sealed record HolderFlowQuarterState(
    DateOnly ReportDate,
    DateTime ComputedAt,
    int CalculationVersion
)
{
    public bool SectorSplitAvailable =>
        CalculationVersion >= HolderSectorFlowCalculator.FirstVersion;
}
