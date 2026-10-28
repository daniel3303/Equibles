using Equibles.Holdings.HostedService.Services;

namespace Equibles.Holdings.HostedService.Models;

/// <summary>
/// One position moved off a straddled split's later basis, with the figures it was filed with.
/// </summary>
internal sealed record SplitBasisCorrection(
    SplitBasisShareCountRepairer.Kind Kind,
    decimal Factor,
    string AccessionNumber,
    string Cusip,
    string ListedTicker,
    DateOnly ReportDate,
    DateOnly FilingDate,
    long FiledShares,
    long FiledValue,
    long? NeighbourShares
);
