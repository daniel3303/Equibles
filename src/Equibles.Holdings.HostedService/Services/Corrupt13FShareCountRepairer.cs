using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Detects and repairs 13F filings whose share-count column duplicates the
/// value column. Some filers' software writes the position's dollar value
/// into <c>sshPrnamt</c> (every row reports <c>sshPrnamt == value</c>);
/// ingesting those counts verbatim and deriving Value = shares × price
/// inflates the portfolio by roughly the share price (~100–1000×), which
/// then dominates the AUM-movers and top-by-AUM rankings.
/// </summary>
internal static class Corrupt13FShareCountRepairer
{
    /// <summary>
    /// 13F values filed on/after this date are whole dollars (SEC's 2022 13F
    /// modernization, effective 2023-01-03); earlier filings report thousands.
    /// </summary>
    internal static readonly DateOnly WholeDollarValueEffectiveDate = new(2023, 1, 3);

    // A filing is only suspect when nearly every comparable row duplicates the
    // value into the share count; scattered equalities (a stock trading at
    // exactly $1.00) must never flag a filing.
    private const double SuspectRowFraction = 0.8;

    // Below this many comparable rows the fraction signal is meaningless — a
    // tiny filing could trip it on price coincidence alone.
    private const int MinimumComparableRows = 5;

    /// <summary>
    /// True when the filing's share-count column duplicates its value column:
    /// at least 80% of comparable rows (share-type rows where both figures are
    /// positive) report the exact same number in both. Principal-type rows are
    /// excluded — a par bond legitimately reports principal ≈ value.
    /// </summary>
    internal static bool IsSuspect(IReadOnlyCollection<BufferedHoldingRow> rows)
    {
        var comparable = 0;
        var duplicated = 0;

        foreach (var row in rows)
        {
            if (row.Holding.ShareType != ShareType.Shares)
                continue;
            if (row.Holding.Shares <= 0 || row.ReportedValue <= 0)
                continue;

            comparable++;
            if (row.Holding.Shares == row.ReportedValue)
                duplicated++;
        }

        return comparable >= MinimumComparableRows && duplicated >= comparable * SuspectRowFraction;
    }

    /// <summary>
    /// Repairs every duplicated row of a suspect filing in place. The value
    /// column is internally consistent in these filings, so the true share
    /// count is value ÷ closing price; without a price the voting-authority
    /// total is used when populated (these filers report the real count
    /// there), and a row with neither anchor is dropped from
    /// <paramref name="rows"/> rather than allowed to poison the rankings.
    /// </summary>
    internal static Corrupt13FRepairOutcome Repair(
        List<BufferedHoldingRow> rows,
        IReadOnlyDictionary<(Guid StockId, DateOnly ReportDate), decimal> stockPrices
    )
    {
        var repaired = 0;
        var dropped = 0;
        var kept = new List<BufferedHoldingRow>(rows.Count);

        foreach (var row in rows)
        {
            if (!IsDuplicatedRow(row))
            {
                kept.Add(row);
                continue;
            }

            var holding = row.Holding;
            var reportedDollars = ToDollars(row.ReportedValue, holding.FilingDate);

            if (
                stockPrices.TryGetValue(
                    (holding.CommonStockId, holding.ReportDate),
                    out var closePrice
                )
                && closePrice > 0
            )
            {
                var shares = (long)Math.Round(reportedDollars / closePrice);
                // The truncating cast deliberately mirrors ParseHoldingRow's
                // `(long)(shares * closePrice)` so a repaired row is identical
                // to what the same filing would have produced if filed correctly.
                ApplyRepair(row, shares, (long)(shares * closePrice), valuePending: false);
                repaired++;
                kept.Add(row);
                continue;
            }

            // Assumes the three authority columns partition the position (the
            // normal 13F shape, and what both reference filings exhibit); a
            // filer reporting overlapping authority would overstate the count,
            // but the row stays ValuePending and is repriced later anyway.
            var votingTotal =
                holding.VotingAuthSole + holding.VotingAuthShared + holding.VotingAuthNone;
            if (votingTotal > 0)
            {
                // No price for this (stock, quarter) yet: keep the corrected
                // count and let the pending-value retry path price it later.
                ApplyRepair(row, votingTotal, 0L, valuePending: true);
                repaired++;
                kept.Add(row);
                continue;
            }

            dropped++;
        }

        rows.Clear();
        rows.AddRange(kept);
        return new Corrupt13FRepairOutcome(repaired, dropped);
    }

    private static bool IsDuplicatedRow(BufferedHoldingRow row) =>
        row.Holding.ShareType == ShareType.Shares
        && row.Holding.Shares > 0
        && row.Holding.Shares == row.ReportedValue;

    private static decimal ToDollars(long reportedValue, DateOnly filingDate) =>
        filingDate < WholeDollarValueEffectiveDate ? reportedValue * 1000m : reportedValue;

    private static void ApplyRepair(
        BufferedHoldingRow row,
        long shares,
        long value,
        bool valuePending
    )
    {
        row.Holding.Shares = shares;
        row.Holding.Value = value;
        row.Holding.ValuePending = valuePending;
        row.ManagerEntry.Shares = shares;
        row.ManagerEntry.Value = value;
    }
}
