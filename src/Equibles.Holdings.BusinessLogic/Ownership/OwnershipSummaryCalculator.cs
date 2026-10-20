using Equibles.Holdings.BusinessLogic.Intelligence;
using Equibles.Holdings.BusinessLogic.Intelligence.Models;
using Equibles.Holdings.BusinessLogic.Ownership.Models;

namespace Equibles.Holdings.BusinessLogic.Ownership;

/// <summary>
/// Sums arranged rows into the ownership summary and flags every row it leaves out NotSummed.
/// </summary>
/// <remarks>
/// Institutional is every 13F filer's common shares. Insiders count on single-class issuers only,
/// leaving out every covered CIK (<see cref="Covered"/>): 13F filers of the listing, holders a 13F
/// filer's report contains, and the persons of a 13D or 13G that names any of them. A 13D or 13G
/// block counts when neither its filer nor a person it names is covered or a counted insider;
/// filings that name one another's filers are one group and count once, at the largest block,
/// since reporting persons are never additive. Funds and non-US rows are never summed. Remaining is
/// shares outstanding minus the summed lines, flagged rather than clipped when negative, and absent
/// with the shares outstanding whenever % Out is unavailable.
/// </remarks>
public static class OwnershipSummaryCalculator
{
    public sealed class Context
    {
        public long? SharesOutstanding { get; init; }

        /// <summary>Why % of shares outstanding is unavailable; null when it is available.</summary>
        public string PercentUnavailableReason { get; init; }

        /// <summary>Why insiders are not summed (a multi-class issuer); null to sum them.</summary>
        public string InsidersNotSummedReason { get; init; }

        public DateOnly? InstitutionalAsOf { get; init; }
        public bool InstitutionalCombined { get; init; }

        /// <summary>The holder a 13F filer buckets as for the breakdowns; null when unknown.</summary>
        public Func<Guid, HolderMixHolder> MixHolder { get; init; } = _ => null;

        public DateOnly? StyleAsOf { get; init; }
    }

    public static StockOwnershipSummary Build(IReadOnlyList<StockHolderRow> rows, Context context)
    {
        // A count in another unit (a foreign private issuer's ordinary shares) or of another scope
        // never meets the listed shares, so it is neither a denominator nor a remainder.
        var outstanding =
            context.PercentUnavailableReason == null ? context.SharesOutstanding : null;
        decimal? Percent(long shares) =>
            outstanding is > 0 ? Math.Round(100m * shares / outstanding.Value, 4) : null;

        var institutional = rows.Where(row => row.Source == StockHolderSource.Form13F).ToList();
        var covered = Covered(rows);

        var insiders = rows.Where(row =>
                row.Source == StockHolderSource.Insider
                && context.InsidersNotSummedReason == null
                && !row.Has(StockHolderRowFlags.Stale)
                && row.Covering13FCik == null
                && (row.Cik == null || !covered.Contains(row.Cik))
            )
            .ToList();
        var insiderCiks = insiders
            .Select(row => row.Cik)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        var blocks = rows.Where(row =>
                IsSchedule(row.Source)
                && !row.Has(StockHolderRowFlags.Stale)
                && row.Covering13FCik == null
                && !Names(row).Any(covered.Contains)
                && !Names(row).Any(insiderCiks.Contains)
            )
            .ToList();
        var counted = LargestPerGroup(blocks);

        var summed = institutional.Concat(insiders).Concat(counted).ToHashSet();
        foreach (var row in rows)
        {
            if (summed.Contains(row))
                row.Flags &= ~StockHolderRowFlags.NotSummed;
            else
                row.Flags |= StockHolderRowFlags.NotSummed;
        }

        var funds = rows.Where(row =>
                row.Source == StockHolderSource.Fund && !row.Has(StockHolderRowFlags.Stale)
            )
            .ToList();
        var institutionalShares = institutional.Sum(row => row.Shares);
        var insiderShares = insiders.Sum(row => row.Shares);
        var blockShares = counted.Sum(row => row.Shares);
        var remaining = outstanding is { } total
            ? total - institutionalShares - insiderShares - blockShares
            : (long?)null;

        return new StockOwnershipSummary
        {
            SharesOutstanding = outstanding,
            PercentUnavailableReason = context.PercentUnavailableReason,
            Institutional = new OwnershipSummaryLine(
                "Institutional (13F)",
                institutionalShares,
                institutional.Count,
                Percent(institutionalShares),
                true
            ),
            Insiders = new OwnershipSummaryLine(
                "Insiders (Forms 3, 4, 5)",
                insiderShares,
                insiders.Count,
                Percent(insiderShares),
                context.InsidersNotSummedReason == null
            ),
            Schedule13DGOnly = new OwnershipSummaryLine(
                "13D/G-only blocks",
                blockShares,
                counted.Count,
                Percent(blockShares),
                true
            ),
            Funds = new OwnershipSummaryLine(
                "Registered funds (N-PORT, not summed)",
                funds.Sum(row => row.Shares),
                funds.Count,
                Percent(funds.Sum(row => row.Shares)),
                false
            ),
            Remaining = remaining is { } left
                ? new OwnershipSummaryLine("Remaining", left, 0, Percent(left), true)
                : null,
            InsidersNotSummedReason = context.InsidersNotSummedReason,
            InstitutionalAsOf = context.InstitutionalAsOf,
            InstitutionalCombined = context.InstitutionalCombined,
            SplitUnresolvedRows = summed.Count(row => row.Has(StockHolderRowFlags.SplitUnresolved)),
            ByHolderType = Breakdown(
                institutional,
                HolderMixDimension.EntityType,
                context,
                Percent
            ),
            ByStyle = Breakdown(institutional, HolderMixDimension.Style, context, Percent),
            ByLocation = Breakdown(institutional, HolderMixDimension.Location, context, Percent),
            StyleAsOf = context.StyleAsOf,
        };
    }

    /// <summary>
    /// The CIKs whose shares a 13F report of the listing already holds: its 13F filers, holders a
    /// filer's report contains, and, until nothing changes, every person of a 13D or 13G holding
    /// shares whose filer or any named person is already covered, since they report one block.
    /// </summary>
    internal static HashSet<string> Covered(IReadOnlyList<StockHolderRow> rows)
    {
        var covered = rows.Where(row =>
                row.Cik != null
                && (row.Source == StockHolderSource.Form13F || row.Covering13FCik != null)
            )
            .Select(row => row.Cik)
            .ToHashSet(StringComparer.Ordinal);
        // A filing reporting no shares (an exit) no longer shows a group holding a block together;
        // a filing older than its filer's 13F still does, so only the exit is left out.
        var schedules = rows.Where(row => IsSchedule(row.Source) && row.Shares > 0).ToList();
        bool added;
        do
        {
            added = false;
            var touching = schedules
                .Select(schedule => Names(schedule).ToList())
                .Where(names => names.Any(covered.Contains))
                .ToList();
            foreach (var cik in touching.SelectMany(names => names))
                added |= covered.Add(cik);
        } while (added);
        return covered;
    }

    // A 13D or 13G's filer and the reporting persons it names, by CIK.
    private static IEnumerable<string> Names(StockHolderRow row) =>
        row.Cik == null ? row.ReportingPersonCiks : row.ReportingPersonCiks.Append(row.Cik);

    // Filings that name one another's filers report one group's shares; only the largest counts.
    private static List<StockHolderRow> LargestPerGroup(List<StockHolderRow> blocks)
    {
        var parent = new Dictionary<string, string>(StringComparer.Ordinal);
        string Find(string key)
        {
            while (parent.TryGetValue(key, out var next) && next != key)
                key = next;
            return key;
        }
        void Union(string a, string b)
        {
            parent.TryAdd(a, a);
            parent.TryAdd(b, b);
            var (rootA, rootB) = (Find(a), Find(b));
            if (rootA != rootB)
                parent[rootB] = rootA;
        }
        string KeyOf(StockHolderRow row) => row.Cik ?? row.RowKey;

        foreach (var block in blocks)
        {
            parent.TryAdd(KeyOf(block), KeyOf(block));
            foreach (var person in block.ReportingPersonCiks)
                Union(KeyOf(block), person);
        }
        return blocks
            .GroupBy(block => Find(KeyOf(block)))
            .Select(group => StockOwnershipPrecedence.Ordered(group).First())
            .ToList();
    }

    private static List<OwnershipBreakdownLine> Breakdown(
        List<StockHolderRow> institutional,
        HolderMixDimension dimension,
        Context context,
        Func<long, decimal?> percent
    ) =>
        institutional
            .Select(row =>
                (
                    Row: row,
                    Bucket: HolderMixBuckets.Of(
                        dimension,
                        (row.InstitutionalHolderId is { } id ? context.MixHolder(id) : null)
                            ?? HolderMixHolder.Of(null, null, null, null, null, row.Country)
                    )
                )
            )
            .GroupBy(entry => entry.Bucket.Token)
            .Select(group =>
            {
                var bucket = group.First().Bucket;
                var shares = group.Sum(entry => entry.Row.Shares);
                return new OwnershipBreakdownLine(
                    bucket.Token,
                    bucket.Label,
                    bucket.Order,
                    shares,
                    group.Count(),
                    percent(shares)
                );
            })
            .OrderByDescending(line => line.Shares)
            .ThenBy(line => line.Order)
            .ToList();

    private static bool IsSchedule(StockHolderSource source) =>
        source is StockHolderSource.Schedule13D or StockHolderSource.Schedule13G;
}
