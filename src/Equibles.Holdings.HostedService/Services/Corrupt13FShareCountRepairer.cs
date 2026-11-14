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
    // A filing is only suspect when nearly every comparable row duplicates the
    // value into the share count; scattered equalities (a stock trading at
    // exactly $1.00) must never flag a filing.
    internal const double SuspectRowFraction = 0.8;

    // Below this many comparable rows the fraction signal is meaningless — a
    // tiny filing could trip it on price coincidence alone.
    internal const int MinimumComparableRows = 5;

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
    /// Repairs every duplicated row of a suspect filing in place. The value column is internally
    /// consistent in these filings, so the true share count is value ÷ the quarter-end price on
    /// the filing's own share basis (<paramref name="filingBasisPrice"/>): the stored close is on
    /// today's basis, and dividing by it states the count on a later split's basis. Without an
    /// honest price the voting-authority total is used when populated (these filers report the
    /// real count there), and a row with neither anchor is dropped from <paramref name="rows"/>
    /// rather than allowed to poison the rankings. <paramref name="revalue"/> re-derives each
    /// repaired row, so nothing of the corrupt count's valuation survives.
    /// </summary>
    internal static Corrupt13FRepairOutcome Repair(
        List<BufferedHoldingRow> rows,
        Func<InstitutionalHolding, decimal?> filingBasisPrice,
        Action<BufferedHoldingRow> revalue
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
            var reportedDollars = FiledValueScale.ToDollars(row.ReportedValue, holding.FilingDate);

            if (filingBasisPrice(holding) is { } price && price > 0)
            {
                var recoveredShares = Math.Round(reportedDollars / price);
                // An oversized corrupt row can push value ÷ price past Int64; it is too large to
                // represent, so drop it like the neither-anchor case below instead of letting the
                // (long) cast throw OverflowException and abort the whole import batch.
                if (recoveredShares < long.MinValue || recoveredShares > long.MaxValue)
                {
                    dropped++;
                    continue;
                }

                ApplyRepair(row, (long)recoveredShares, revalue);
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
                // No honest price for this (stock, quarter) yet: keep the corrected count and
                // let the pending-value retry path price it later.
                ApplyRepair(row, votingTotal, revalue);
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

    private static void ApplyRepair(
        BufferedHoldingRow row,
        long shares,
        Action<BufferedHoldingRow> revalue
    )
    {
        row.Holding.Shares = shares;
        row.ManagerEntry.Shares = shares;
        revalue(row);
    }
}
