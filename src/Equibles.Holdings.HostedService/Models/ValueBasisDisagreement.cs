namespace Equibles.Holdings.HostedService.Models;

/// <summary>
/// One position whose derived value disagrees grossly with the value its filer reported. Carries
/// the share count because the ratio between the two figures is usually the diagnosis: a split
/// ratio, or a depositary ratio, applied to the count.
/// </summary>
public record ValueBasisDisagreement(
    string Cusip,
    DateOnly ReportDate,
    long Shares,
    long DerivedValue,
    decimal FiledValue
);
