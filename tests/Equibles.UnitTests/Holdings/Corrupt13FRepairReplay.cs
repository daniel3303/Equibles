using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// The duplicated-column repair wired the way the import wires it for a series with no split
/// after the quarter: the stored close is the filing-basis price, and a repaired row is valued at
/// shares × close or left pending without one.
/// </summary>
internal static class Corrupt13FRepairReplay
{
    public static Corrupt13FRepairOutcome Repair(
        List<BufferedHoldingRow> rows,
        IReadOnlyDictionary<(Guid, string, DateOnly), decimal> closes
    ) =>
        Corrupt13FShareCountRepairer.Repair(
            rows,
            holding =>
                Close(closes, holding.EquityIssuerId, holding.ListedTicker, holding.ReportDate),
            row =>
            {
                var holding = row.Holding;
                var close = Close(
                    closes,
                    holding.EquityIssuerId,
                    holding.ListedTicker,
                    holding.ReportDate
                );
                var value = close is { } price ? (long)(holding.Shares * price) : 0L;
                holding.Value = value;
                holding.ValuePending = close == null;
                row.ManagerEntry.Value = value;
            }
        );

    private static decimal? Close(
        IReadOnlyDictionary<(Guid, string, DateOnly), decimal> closes,
        Guid issuerId,
        string listedTicker,
        DateOnly reportDate
    ) => closes.TryGetValue((issuerId, listedTicker, reportDate), out var close) ? close : null;
}
