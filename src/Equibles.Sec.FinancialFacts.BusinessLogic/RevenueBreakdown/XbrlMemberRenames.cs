namespace Equibles.Sec.FinancialFacts.BusinessLogic.RevenueBreakdown;

/// <summary>
/// Unifies one line an issuer tagged under two extension QNames across its own filings,
/// beyond what the case/underscore fold can see. Palantir tagged pltr:CommercialMember in
/// every 10-K but pltr:CommercialSegmentMember in the FY2024 one; ReconcileToTotal keeps
/// each period's LATEST filing, so FY2022 survived only under the FY2024 spelling and the
/// pivot split one segment into a "Commercial" row with a hole and a one-cell
/// "Commercial Segment" row (EquiblesCommercial, PLTR review 2026-09-19).
///
/// No name is compared. Two members are the same line only by arithmetic, the bar
/// KpiSegmentRenameContinuity sets for the KPI splice: they never appear together in one
/// filing (two members tagged side by side are two members), they overlap on at least
/// <see cref="MinimumOverlappingPeriods"/> exact (start, end, unit) periods, every overlap
/// states the same positive figure to the cent (a restated comparative repeats the number;
/// a re-segmentation does not), and no overlap contradicts. A period one filing states
/// twice with two figures is no evidence either way, so the verdict never depends on the
/// order rows arrive in. One unequal overlap disproves
/// the pair outright and dissolves any group it would have joined through a bridging
/// member. Proven pairs union transitively; the representative is the latest-filed
/// spelling, the same choice BuildAxisSeries makes for a fold group, so a rewritten
/// row's Member is what the axis would have shown anyway.
/// </summary>
public static class XbrlMemberRenames
{
    // Two exactly-equal positive overlaps prove a rename; one could be a round-value
    // coincidence. The same bar ArithmeticRollupMembers and the KPI splice use.
    public const int MinimumOverlappingPeriods = 2;

    // Combinatorial guard: pairs are enumerated exhaustively, so an axis wider than this
    // is returned exactly as tagged. Breakdown axes carry well under this in practice.
    public const int MaxMembers = 64;

    public static List<DimensionalRevenueRow> Unify(IReadOnlyList<DimensionalRevenueRow> rows)
    {
        if (rows == null)
        {
            return [];
        }
        var members = rows.GroupBy(r => XbrlMemberNames.Fold(r.Member), StringComparer.Ordinal)
            .Select(g => new MemberEvidence(
                g.Key,
                g.Select(r => r.FiledDate).ToHashSet(),
                g.GroupBy(r => (r.PeriodStart, r.PeriodEnd, r.Unit))
                    .ToDictionary(p => p.Key, LatestStatedValue),
                g.OrderByDescending(r => r.FiledDate)
                    .ThenByDescending(r => r.PeriodEnd)
                    .ThenBy(r => r.Member, StringComparer.Ordinal)
                    .First()
            ))
            .ToList();
        if (members.Count < 2 || members.Count > MaxMembers)
        {
            return rows.ToList();
        }

        var parent = Enumerable.Range(0, members.Count).ToArray();
        var disproven = new List<(int, int)>();
        for (var i = 0; i < members.Count; i++)
        {
            for (var j = i + 1; j < members.Count; j++)
            {
                switch (Judge(members[i], members[j]))
                {
                    case Verdict.Proven:
                        Union(parent, i, j);
                        break;
                    case Verdict.Contradicted:
                    case Verdict.CoFiled:
                        disproven.Add((i, j));
                        break;
                }
            }
        }

        // A disproven pair that still landed in one group (through a bridging member)
        // dissolves the whole group: two spellings tagged side by side, or stating
        // different figures, cannot be one line however a third spelling matches both.
        var dissolved = disproven
            .Where(pair => Find(parent, pair.Item1) == Find(parent, pair.Item2))
            .Select(pair => Find(parent, pair.Item1))
            .ToHashSet();

        // Only a group that actually joined two spellings rewrites anything; a lone
        // spelling keeps its rows byte-identical.
        var representative = new Dictionary<int, string>();
        foreach (var group in Enumerable.Range(0, members.Count).GroupBy(i => Find(parent, i)))
        {
            if (dissolved.Contains(group.Key) || group.Count() < 2)
            {
                continue;
            }
            representative[group.Key] = group
                .Select(i => members[i].Latest)
                .OrderByDescending(r => r.FiledDate)
                .ThenByDescending(r => r.PeriodEnd)
                .ThenBy(r => r.Member, StringComparer.Ordinal)
                .First()
                .Member;
        }
        if (representative.Count == 0)
        {
            return rows.ToList();
        }

        var indexByFold = members
            .Select((m, index) => (m.Fold, index))
            .ToDictionary(t => t.Fold, t => t.index, StringComparer.Ordinal);
        return rows.Select(r =>
            {
                var group = Find(parent, indexByFold[XbrlMemberNames.Fold(r.Member)]);
                return
                    representative.TryGetValue(group, out var member)
                    && !string.Equals(member, r.Member, StringComparison.Ordinal)
                    ? r with
                    {
                        Member = member,
                    }
                    : r;
            })
            .ToList();
    }

    private enum Verdict
    {
        Unproven,
        Proven,
        Contradicted,
        CoFiled,
    }

    private static Verdict Judge(MemberEvidence a, MemberEvidence b)
    {
        // Tagged side by side in one filing: two members, whatever their values say.
        if (a.FiledDates.Overlaps(b.FiledDates))
        {
            return Verdict.CoFiled;
        }
        var matches = 0;
        foreach (var (period, value) in a.LatestByPeriod)
        {
            if (
                value == null
                || !b.LatestByPeriod.TryGetValue(period, out var other)
                || other == null
            )
            {
                continue;
            }
            if (value != other)
            {
                return Verdict.Contradicted;
            }
            if (value > 0m)
            {
                matches++;
            }
        }
        return matches >= MinimumOverlappingPeriods ? Verdict.Proven : Verdict.Unproven;
    }

    private static int Find(int[] parent, int i)
    {
        while (parent[i] != i)
        {
            parent[i] = parent[parent[i]];
            i = parent[i];
        }
        return i;
    }

    private static void Union(int[] parent, int a, int b)
    {
        var rootA = Find(parent, a);
        var rootB = Find(parent, b);
        if (rootA != rootB)
        {
            parent[Math.Max(rootA, rootB)] = Math.Min(rootA, rootB);
        }
    }

    // The figure a member's latest filing states for one period, or null when that
    // filing states the period twice with different figures (a segment total tagged
    // both with and without the consolidation qualifier): such a period is no
    // evidence either way, which also keeps the verdict independent of row order.
    private static decimal? LatestStatedValue(IEnumerable<DimensionalRevenueRow> period)
    {
        var latest = period.Max(r => r.FiledDate);
        var values = period
            .Where(r => r.FiledDate == latest)
            .Select(r => r.Value)
            .Distinct()
            .ToList();
        return values.Count == 1 ? values[0] : null;
    }

    private sealed record MemberEvidence(
        string Fold,
        HashSet<DateOnly> FiledDates,
        Dictionary<
            (DateOnly? PeriodStart, DateOnly PeriodEnd, string Unit),
            decimal?
        > LatestByPeriod,
        DimensionalRevenueRow Latest
    );
}
