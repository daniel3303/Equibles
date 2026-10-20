using Equibles.InsiderTrading.Data.Extensions;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Repositories.Models;

namespace Equibles.InsiderTrading.Repositories;

/// <summary>
/// Splits one filing's rows into the balances it reports. Direct holdings of a security are one
/// balance. Indirect rows are grouped by their filed nature of ownership, and within a group a row
/// continues the balance its trade started from (shares owned after, less the shares acquired or
/// plus those disposed); a holding row is a separate balance unless it repeats one. Footnote ids
/// only break ties: filers attach one-off footnotes to single trades of one vehicle. A holding
/// line whose balance footnotes share nothing with the footnotes on the balance it equals names
/// another vehicle; trades never split on balance footnotes, which often explain a jump instead.
/// </summary>
public static class InsiderPositionBalances
{
    // Fractional share counts are rounded on their own, so a continued balance may be off by one.
    private const long RoundingTolerance = 1;

    public static InsiderPositionClosings Resolve(IEnumerable<InsiderTransaction> rows)
    {
        var closings = new List<InsiderTransaction>();
        var ambiguous = false;
        foreach (
            var security in rows.GroupBy(t => (Title: t.SecurityTitle ?? "", t.OwnershipNature))
        )
        {
            if (security.Key.OwnershipNature == OwnershipNature.Direct)
            {
                closings.Add(security.AsQueryable().OrderCurrentPositionFirst().First());
                continue;
            }

            foreach (
                var vehicle in security.GroupBy(t =>
                    (Unnamed: IsUnnamed(t), Nature: (t.NatureOfOwnership ?? "").ToUpperInvariant())
                )
            )
            {
                var chains = Chain(
                    vehicle.OrderBy(t => t.TransactionOrder).ThenBy(t => t.Id).ToList(),
                    out var guessed
                );
                if (vehicle.Key.Unnamed)
                {
                    // Rows parsed before vehicles were recorded keep one balance, the newest.
                    closings.Add(vehicle.AsQueryable().OrderCurrentPositionFirst().First());
                    ambiguous |= guessed || chains.Count > 1;
                    continue;
                }

                closings.AddRange(chains.Select(chain => chain[^1]));
                ambiguous |=
                    guessed
                    || chains
                        .Select(chain => FootnoteKey(chain[0]))
                        .GroupBy(key => key)
                        .Any(group => group.Count() > 1);
            }
        }

        return new InsiderPositionClosings(closings, ambiguous);
    }

    // Each chain is one vehicle's rows in filing order; its last row holds the closing balance.
    private static List<List<InsiderTransaction>> Chain(
        List<InsiderTransaction> rows,
        out bool guessed
    )
    {
        guessed = false;
        var chains = new List<List<InsiderTransaction>>();
        foreach (var row in rows)
        {
            var key = FootnoteKey(row);
            List<InsiderTransaction> target;
            if (row.TransactionCode == TransactionCode.Holding)
            {
                target = chains.LastOrDefault(chain =>
                    Continues(chain, row.SharesOwnedAfter)
                    && chain.Any(member => FootnoteKey(member) == key)
                    && !NamesAnotherBalance(row, chain[^1])
                );
            }
            else
            {
                var before = BalanceBefore(row);
                var continued = chains.Where(chain => Continues(chain, before)).ToList();
                target =
                    continued.LastOrDefault(chain =>
                        chain.Any(member => FootnoteKey(member) == key)
                    ) ?? continued.LastOrDefault();
                if (target == null)
                {
                    target = chains.LastOrDefault(chain =>
                        chain.Any(member => FootnoteKey(member) == key)
                    );
                    guessed |= target != null;
                }
            }

            if (target == null)
            {
                target = [];
                chains.Add(target);
            }
            target.Add(row);
        }
        return chains;
    }

    // Both balances are footnoted, by different notes: the filer says which vehicle each one is.
    private static bool NamesAnotherBalance(InsiderTransaction holding, InsiderTransaction earlier)
    {
        var mine = holding.BalanceFootnoteIds ?? [];
        var theirs = earlier.BalanceFootnoteIds ?? [];
        return mine.Count > 0 && theirs.Count > 0 && !mine.Intersect(theirs).Any();
    }

    private static bool Continues(List<InsiderTransaction> chain, long balance) =>
        Math.Abs(chain[^1].SharesOwnedAfter - balance) <= RoundingTolerance;

    private static long BalanceBefore(InsiderTransaction row) =>
        row.AcquiredDisposed == AcquiredDisposed.Acquired
            ? row.SharesOwnedAfter - row.Shares
            : row.SharesOwnedAfter + row.Shares;

    private static bool IsUnnamed(InsiderTransaction row) =>
        string.IsNullOrEmpty(row.NatureOfOwnership)
        && (row.OwnershipNatureFootnoteIds == null || row.OwnershipNatureFootnoteIds.Count == 0);

    private static string FootnoteKey(InsiderTransaction row) =>
        string.Join(',', (row.OwnershipNatureFootnoteIds ?? []).Order(StringComparer.Ordinal));
}
