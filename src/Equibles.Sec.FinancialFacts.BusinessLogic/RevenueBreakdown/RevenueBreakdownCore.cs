using System.Globalization;
using System.Text.RegularExpressions;

namespace Equibles.Sec.FinancialFacts.BusinessLogic.RevenueBreakdown;

/// <summary>
/// The ONE selection lane for revenue disaggregated by business segment, geography and
/// product/service from dimensional XBRL facts. Every surface serving a revenue
/// breakdown — the MCP tool, the commercial REST endpoint and the portal — must build
/// its member sets and values through this class, so the transports can never disagree
/// on what a company's segments are (EquiblesCommercial#7166). Consumers keep their own
/// queries and rendering; the selection rules live here.
/// </summary>
public static class RevenueBreakdownCore
{
    // Issuers moved geography/product from us-gaap to the srt taxonomy in 2018; both
    // QNames identify the same axis, so each bucket accepts both spellings.
    public static readonly string[] SegmentAxes = ["us-gaap:StatementBusinessSegmentsAxis"];
    public static readonly string[] GeographyAxes =
    [
        "srt:StatementGeographicalAxis",
        "us-gaap:StatementGeographicalAxis",
    ];
    public static readonly string[] ProductAxes =
    [
        "srt:ProductOrServiceAxis",
        "us-gaap:ProductOrServiceAxis",
    ];
    public static readonly string[] AllAxes = [.. SegmentAxes, .. GeographyAxes, .. ProductAxes];

    // The canonical family order — used only to break ties deterministically when two
    // partner families score identically in the cross-cut roll-up.
    public static readonly string[][] AxisFamilies = [SegmentAxes, GeographyAxes, ProductAxes];

    // srt:ConsolidationItemsAxis = us-gaap:OperatingSegmentsMember is a transparent
    // qualifier ("this figure is a pure operating-segment total"), not a second slice of
    // the value. A fact carrying it alongside one real axis still partitions total revenue,
    // so it must not be discarded as a cross-cut. Issuers such as Apple tag every operating
    // segment this way from FY2025 on, which otherwise drops the latest fiscal year (#3628).
    public const string ConsolidationItemsAxis = "srt:ConsolidationItemsAxis";
    public const string OperatingSegmentsMember = "us-gaap:OperatingSegmentsMember";

    // How close a member set must sum to consolidated total revenue to count as a
    // complete re-disaggregation (see ReconcileToTotal). Half a percent absorbs
    // rounding and minor unit scaling without admitting a partial amendment.
    public const decimal ReconciliationTolerance = 0.005m;

    // Combinatorial guards for the overlapping-scheme collapse (see FindFullTotalPartition).
    // Three independent bounds, each with its own job:
    //  - MaxCollapseMembers = 31 keeps every index inside the 32 bits the subset bitmasks
    //    address — past 32 members `1 << i` aliases (the shift count wraps) and the cover
    //    search degenerated into a non-terminating loop. 31 deliberately does NOT try to
    //    bound cost: a 13-20-member axis is cheap to search and collapsing it is
    //    load-bearing (#3897 double-count), so a tighter cap would silently re-open that
    //    bug there.
    //  - MaxSubsetSearchNodes is the cost bound. Node count ≤ 2^members, so from ~18
    //    near-equal members the enumeration can pass 200k nodes; the budget stops it in
    //    bounded time regardless of shape, and an exhausted budget discards the
    //    (incomplete) result.
    //  - MaxCoverSubsets bounds the cover search's input. ≤ 2^12 keeps FindBestCover no
    //    worse than it ever was under the search's original "well under 15 members"
    //    assumption; a genuine overlap yields a handful of full-total subsets.
    private const int MaxCollapseMembers = 31;
    private const int MaxSubsetSearchNodes = 200_000;
    private const int MaxCoverSubsets = 4_096;

    /// <summary>
    /// Rolls TWO-dimensional cross-cut facts up onto one axis family, ONLY for periods
    /// where the family has no single-axis cut (a reported single-axis disaggregation
    /// always beats a derived roll-up). Filers can move a cut to two-dimensional tagging
    /// entirely (SoFi tags every product fact product × segment from FY2023; XOM tags
    /// segments and geographies only in cross-cuts after its 2022 reorganisation), which
    /// the single-axis query rightly excludes — without the roll-up those axes freeze at
    /// the last single-axis fiscal year.
    ///
    /// The roll-up NEVER mixes partner families: a member's legs are summed over exactly
    /// one partner axis family per period. Summing legs from several partner families
    /// counts the same revenue once per family — XOM's geography axis published ~2.34×
    /// consolidated revenue that way (product×geo legs plus geo×segment legs both
    /// credited to each country; EquiblesCommercial#7166). One partner family is chosen
    /// per (period, unit): the family whose member sum reconciles best against the
    /// consolidated totals — reconciling within tolerance wins outright, otherwise the
    /// smallest relative deviation; ties break on more members (the more granular view),
    /// then canonical family order. Per (member, partner-member) the latest-filed leg
    /// wins before the sum — the standard restatement rule — and the rolled row carries
    /// the newest leg's filing date with the smallest leg fiscal year as its identity.
    /// </summary>
    public static List<DimensionalRevenueRow> RollUpCrossCuts(
        IReadOnlyList<CrossCutRevenueRow> crossCuts,
        string[] family,
        IReadOnlyList<DimensionalRevenueRow> singleAxisRows,
        IReadOnlyDictionary<(DateOnly PeriodEnd, string Unit), IReadOnlyList<decimal>> totals
    )
    {
        var coveredPeriods = singleAxisRows
            .Where(r => family.Contains(r.Axis))
            .Select(r => r.PeriodEnd)
            .ToHashSet();

        // Orient each cross-cut: exactly one side on the requested family, the other on a
        // DIFFERENT family (a cross-cut inside one family would double-count it).
        var oriented = crossCuts
            .Select(c =>
                family.Contains(c.AxisA) && !family.Contains(c.AxisB)
                    ? new
                    {
                        OwnAxis = c.AxisA,
                        OwnMember = c.MemberA,
                        OtherAxis = c.AxisB,
                        OtherMember = c.MemberB,
                        Row = c,
                    }
                : family.Contains(c.AxisB) && !family.Contains(c.AxisA)
                    ? new
                    {
                        OwnAxis = c.AxisB,
                        OwnMember = c.MemberB,
                        OtherAxis = c.AxisA,
                        OtherMember = c.MemberA,
                        Row = c,
                    }
                : null
            )
            .Where(c => c != null && !coveredPeriods.Contains(c.Row.PeriodEnd))
            .Select(c => new
            {
                c.OwnAxis,
                c.OwnMember,
                c.OtherAxis,
                c.OtherMember,
                c.Row,
                PartnerFamily = PartnerFamilyIndex(c.OtherAxis),
            })
            .Where(c => c.PartnerFamily >= 0)
            .ToList();

        var result = new List<DimensionalRevenueRow>();
        foreach (var periodGroup in oriented.GroupBy(c => (c.Row.PeriodEnd, c.Row.Unit)))
        {
            // One rolled candidate per partner family present in this period.
            var candidates = periodGroup
                .GroupBy(c => c.PartnerFamily)
                .Select(fg => new
                {
                    Family = fg.Key,
                    Rows = fg.GroupBy(c => (c.OwnAxis, c.OwnMember))
                        .Select(mg =>
                        {
                            var legs = mg.GroupBy(c => (c.OtherAxis, c.OtherMember))
                                .Select(leg =>
                                    leg.OrderByDescending(c => c.Row.FiledDate).First().Row
                                )
                                .ToList();
                            return new DimensionalRevenueRow(
                                mg.Key.OwnAxis,
                                mg.Key.OwnMember,
                                periodGroup.Key.PeriodEnd,
                                legs.Sum(l => l.Value),
                                periodGroup.Key.Unit,
                                legs.Max(l => l.FiledDate),
                                legs.Min(l => l.PeriodStart),
                                legs.Min(l => l.FiscalYear)
                            );
                        })
                        .ToList(),
                })
                .ToList();

            var totalCandidates = totals.TryGetValue(periodGroup.Key, out var totalsForPeriod)
                ? totalsForPeriod
                : [];
            var best = candidates
                .OrderBy(c => ReconciliationScore(c.Rows.Sum(r => r.Value), totalCandidates))
                .ThenByDescending(c => c.Rows.Count)
                .ThenBy(c => c.Family)
                .First();
            result.AddRange(best.Rows);
        }
        return result;
    }

    // The index of the family an axis belongs to, -1 for an unknown axis. Drives the
    // per-partner grouping and the deterministic tie-break in RollUpCrossCuts.
    private static int PartnerFamilyIndex(string axis)
    {
        for (var i = 0; i < AxisFamilies.Length; i++)
        {
            if (AxisFamilies[i].Contains(axis))
            {
                return i;
            }
        }
        return -1;
    }

    // Relative deviation of a candidate member sum from its NEAREST consolidated total —
    // the roll-up's selection metric. No totals to compare against scores worst, so a
    // validated candidate always beats an unvalidatable one.
    private static decimal ReconciliationScore(decimal sum, IReadOnlyList<decimal> totals)
    {
        var best = decimal.MaxValue;
        foreach (var total in totals)
        {
            if (total == 0m)
            {
                continue;
            }
            var deviation = Math.Abs(sum - total) / Math.Abs(total);
            if (deviation < best)
            {
                best = deviation;
            }
        }
        return best;
    }

    /// <summary>
    /// Resolve the surviving (member, period) facts for one axis, one row per cell — all
    /// in a single pinned unit. The default rule keeps the latest-filed fact per
    /// (member, period) — correct for a restatement that re-reports a member with a new
    /// value. It is wrong when a later filing instead *drops* a member it has
    /// reclassified away (NVDA's Singapore, AMD's Japan/Europe, KO/CAT renames): nothing
    /// supersedes the dropped member, so it lingers from the older filing and the axis
    /// double-counts it.
    ///
    /// The fix is arithmetic, not pattern-matching: for each period, if the latest
    /// filing's own members already reconcile to a consolidated total revenue (in the
    /// same unit), that filing is a complete re-disaggregation — use only its members
    /// and discard anything carried over from older filings. Otherwise the latest filing
    /// only restated some members (a partial amendment), so fall back to the
    /// latest-filed-per-member merge that carries un-amended members forward.
    /// </summary>
    public static List<DimensionalRevenueRow> ReconcileToTotal(
        IEnumerable<DimensionalRevenueRow> axisRowsInUnit,
        string unit,
        IReadOnlyDictionary<(DateOnly PeriodEnd, string Unit), IReadOnlyList<decimal>> totals
    )
    {
        var result = new List<DimensionalRevenueRow>();
        foreach (var period in axisRowsInUnit.GroupBy(r => r.PeriodEnd))
        {
            var latestFiled = period.Max(r => r.FiledDate);
            var latestFiling = period.Where(r => r.FiledDate == latestFiled).ToList();
            var latestSum = latestFiling.Sum(r => r.Value);

            // Compared against the consolidated total in the SAME unit as the pinned
            // members. Several revenue concepts may each report a total; the members
            // complete the period if they reconcile to any one of them (e.g. ASC 606
            // revenue vs. total Revenues).
            var candidates = totals.TryGetValue((period.Key, unit), out var totalsForPeriod)
                ? totalsForPeriod
                : [];
            var complete = candidates.Any(total =>
                total != 0m
                && Math.Abs(latestSum - total) <= Math.Abs(total) * ReconciliationTolerance
            );

            List<DimensionalRevenueRow> periodRows;
            if (complete)
            {
                // Latest filing re-disaggregates the whole period — keep only its members,
                // collapsing any duplicate member within the same filing to its first fact.
                periodRows = latestFiling.GroupBy(r => r.Member).Select(g => g.First()).ToList();
            }
            else
            {
                // Partial amendment — latest-filed fact wins per member, older members
                // carried.
                periodRows = period
                    .GroupBy(r => r.Member)
                    .Select(g => g.OrderByDescending(r => r.FiledDate).First())
                    .ToList();
            }

            // An issuer can tag two overlapping disaggregation schemes on the same axis
            // in one filing (e.g. a regional partition AND a by-country partition), each
            // summing to consolidated total revenue. Both survive the merge above, so the
            // axis would show ~2x actual revenue (#3897). Collapse to one scheme when the
            // members partition cleanly into 2+ full-total subsets; otherwise leave the
            // period untouched.
            result.AddRange(CollapseOverlappingSchemes(periodRows, candidates));
        }
        return result;
    }

    // When the members of one period partition into 2+ DISJOINT subsets that EACH
    // reconcile to a consolidated total revenue (with no member left over), the issuer
    // tagged multiple overlapping schemes on the same axis. Keep only the MOST GRANULAR
    // full-total subset (the most members — the most informative view); every full-total
    // subset is individually correct, so this only chooses which correct view to show,
    // never alters a number.
    //
    // Pure arithmetic, no member-name matching. The guard is strict: act only when the
    // members FULLY partition into >=2 disjoint full-total subsets. A single scheme, or a
    // partial overlap with no clean second full-total subset, is left unchanged — nothing
    // is dropped.
    private static List<DimensionalRevenueRow> CollapseOverlappingSchemes(
        List<DimensionalRevenueRow> periodRows,
        IReadOnlyList<decimal> candidates
    )
    {
        if (periodRows.Count < 2)
        {
            return periodRows;
        }

        foreach (var total in candidates.Where(t => t != 0m).Distinct())
        {
            var tolerance = Math.Abs(total) * ReconciliationTolerance;

            // Skip the trivial case where the whole member set already equals the total —
            // that is one scheme, not an overlap of two.
            if (Math.Abs(periodRows.Sum(r => r.Value) - total) <= tolerance)
            {
                continue;
            }

            var partition = FindFullTotalPartition(periodRows, total, tolerance);
            if (partition != null && partition.Count >= 2)
            {
                // Keep the most granular subset; ties broken by the larger summed value,
                // then a stable member ordering, so the choice is deterministic.
                return partition
                    .OrderByDescending(subset => subset.Count)
                    .ThenByDescending(subset => subset.Sum(r => r.Value))
                    .ThenBy(subset => string.Join("|", subset.Select(r => r.Member).Order()))
                    .First();
            }
        }

        return periodRows;
    }

    // Partition the members into disjoint subsets that each sum to `total` (within
    // tolerance), covering every member exactly once. Returns the cover that minimises
    // the total deviation from `total` across its subsets — so the genuine schemes (each
    // summing to the exact consolidated figure) win over a tolerance-admitted near-miss
    // that stitches members from different schemes together. Returns null when no full
    // cover exists (not a clean overlap) or when any combinatorial bound trips — the
    // period then passes through exactly as reported, the search's existing fail-safe.
    private static List<List<DimensionalRevenueRow>> FindFullTotalPartition(
        List<DimensionalRevenueRow> periodRows,
        decimal total,
        decimal tolerance
    )
    {
        // Bitmask-width guard: 31 keeps every index inside the 32 bits the masks address.
        if (periodRows.Count > MaxCollapseMembers)
        {
            return null;
        }

        // Order once so every subset and the final cover are produced deterministically.
        var members = periodRows
            .OrderByDescending(r => r.Value)
            .ThenBy(r => r.Member, StringComparer.Ordinal)
            .ToList();

        // All subsets summing to total within tolerance, each as a bitmask over
        // `members`. The subset-count cap keeps the cover search's input no larger than
        // it ever was under the original assumption; a genuine overlap yields a handful
        // of full-total subsets.
        var fullTotalSubsets = new List<(int Mask, decimal Deviation)>();
        var budget = MaxSubsetSearchNodes;
        EnumerateFullTotalSubsets(
            members,
            0,
            0,
            0m,
            total,
            tolerance,
            fullTotalSubsets,
            ref budget
        );
        if (fullTotalSubsets.Count == 0 || budget <= 0 || fullTotalSubsets.Count > MaxCoverSubsets)
        {
            return null;
        }

        var (bestMasks, found) = FindBestCover(members.Count, fullTotalSubsets);
        if (!found)
        {
            return null;
        }

        return bestMasks
            .Select(mask =>
                Enumerable
                    .Range(0, members.Count)
                    .Where(i => (mask & (1 << i)) != 0)
                    .Select(i => members[i])
                    .ToList()
            )
            .ToList();
    }

    // Depth-first enumeration of every subset of members[startIndex..] whose running sum
    // reaches `total` within tolerance, recorded as a bitmask plus its absolute deviation
    // from total.
    //
    // `budget` counts down the visited nodes across the whole recursion: a set of
    // near-equal members can explore a large share of 2^n even under the member cap, so
    // the budget stops the walk in bounded time regardless of shape. An exhausted budget
    // means the enumeration is incomplete, so the caller must discard the result rather
    // than act on a partial list.
    private static void EnumerateFullTotalSubsets(
        List<DimensionalRevenueRow> members,
        int startIndex,
        int mask,
        decimal sum,
        decimal total,
        decimal tolerance,
        List<(int Mask, decimal Deviation)> output,
        ref int budget
    )
    {
        if (budget <= 0)
        {
            return;
        }
        budget--;

        if (mask != 0 && Math.Abs(sum - total) <= tolerance)
        {
            output.Add((mask, Math.Abs(sum - total)));
            // A superset only adds positive values, moving further from total — so stop
            // here. Negative members (segment operating losses) can under-enumerate past
            // this prune, which only ever SKIPS a collapse — any partition returned still
            // passes the full-total checks, so a wrong collapse remains impossible.
            return;
        }
        if (sum - total > tolerance)
        {
            return;
        }

        for (var i = startIndex; i < members.Count; i++)
        {
            EnumerateFullTotalSubsets(
                members,
                i + 1,
                mask | (1 << i),
                sum + members[i].Value,
                total,
                tolerance,
                output,
                ref budget
            );
            if (budget <= 0)
            {
                return;
            }
        }
    }

    // Choose the disjoint cover of all members (each index used once) built from the
    // full-total subsets, minimising summed deviation, then preferring more subsets.
    // Anchors each step on the lowest uncovered index so the search is forced and
    // bounded.
    private static (List<int> Masks, bool Found) FindBestCover(
        int memberCount,
        List<(int Mask, decimal Deviation)> subsets
    )
    {
        var allCovered = (1 << memberCount) - 1;
        List<int> best = null;
        var bestDeviation = decimal.MaxValue;

        void Search(int covered, List<int> chosen, decimal deviation)
        {
            if (deviation >= bestDeviation)
            {
                return;
            }
            if (covered == allCovered)
            {
                if (
                    best == null
                    || deviation < bestDeviation
                    || (deviation == bestDeviation && chosen.Count > best.Count)
                )
                {
                    best = [.. chosen];
                    bestDeviation = deviation;
                }
                return;
            }

            var anchor = 0;
            while ((covered & (1 << anchor)) != 0)
            {
                anchor++;
            }

            foreach (var (mask, dev) in subsets)
            {
                if ((mask & (1 << anchor)) != 0 && (mask & covered) == 0)
                {
                    chosen.Add(mask);
                    Search(covered | mask, chosen, deviation + dev);
                    chosen.RemoveAt(chosen.Count - 1);
                }
            }
        }

        Search(0, [], 0m);
        return (best ?? [], best != null);
    }

    /// <summary>
    /// Pivot one axis's rows into period-end columns (oldest first) × member rows, all in
    /// a single pinned unit (the latest-filed fact's, so a reporting-currency change
    /// can't mix currencies in one series). Filings re-report comparative prior years, so
    /// the latest-filed fact wins per (member, period-end); ReconcileToTotal runs on the
    /// single-unit rows first. Members merge under the XbrlMemberNames fold — an issuer
    /// respelling its own extension QName across filings (amd:DatacenterMember /
    /// amd:DataCenterMember) otherwise splits one member into two half-series — with the
    /// latest-filed spelling fronting the merged series. Arithmetic rollup members
    /// (subtotals an issuer tags ALONGSIDE their components — INTC's combined
    /// "CCG + DCAI + NEX", AAPL's parent Product line) are dropped last: they escape
    /// CollapseOverlappingSchemes because the subtotal plus the leftover members never
    /// partition into two full-total subsets, and the proof needs the same member's
    /// values across periods, so it runs on the pivoted members.
    /// </summary>
    public static AxisSeries BuildAxisSeries(
        List<DimensionalRevenueRow> rows,
        string[] axes,
        int maxYears,
        IReadOnlyDictionary<(DateOnly PeriodEnd, string Unit), IReadOnlyList<decimal>> totals
    )
    {
        var axisRows = rows.Where(r => axes.Contains(r.Axis)).ToList();
        if (axisRows.Count == 0)
        {
            return new AxisSeries(null, [], []);
        }

        // Pin the unit first (latest-filed fact's unit) so the reconciliation sum and the
        // consolidated total are always in the same currency.
        var unit = axisRows.OrderByDescending(r => r.FiledDate).First().Unit;
        var inUnit = axisRows.Where(r => r.Unit == unit);
        var current = ReconcileToTotal(inUnit, unit, totals);
        if (current.Count == 0)
        {
            return new AxisSeries(null, [], []);
        }

        var periodEnds = current
            .Select(r => r.PeriodEnd)
            .Distinct()
            .OrderByDescending(d => d)
            .Take(maxYears)
            .OrderBy(d => d)
            .ToList();

        var latest = periodEnds[^1];
        var members = current
            .GroupBy(r => XbrlMemberNames.Fold(r.Member), StringComparer.Ordinal)
            .Select(g => new
            {
                Key = g.OrderByDescending(r => r.FiledDate)
                    .ThenByDescending(r => r.PeriodEnd)
                    .First()
                    .Member,
                ByPeriod = g.GroupBy(r => r.PeriodEnd)
                    .ToDictionary(
                        pg => pg.Key,
                        pg => pg.OrderByDescending(r => r.FiledDate).First()
                    ),
            })
            .Select(m => new
            {
                m.Key,
                Latest = m.ByPeriod.TryGetValue(latest, out var latestRow)
                    ? latestRow.Value
                    : (decimal?)null,
                Values = periodEnds
                    .Select(p =>
                        m.ByPeriod.TryGetValue(p, out var row) ? row.Value : (decimal?)null
                    )
                    .ToList(),
                PeriodStarts = periodEnds
                    .Select(p =>
                        m.ByPeriod.TryGetValue(p, out var row) ? row.PeriodStart : (DateOnly?)null
                    )
                    .ToList(),
            })
            .Where(m => m.Values.Any(v => v.HasValue))
            .OrderByDescending(m => m.Latest ?? decimal.MinValue)
            .ThenBy(m => m.Key)
            .Select(m => new AxisMemberSeries(m.Key, Humanize(m.Key), m.Values, m.PeriodStarts))
            .ToList();

        return new AxisSeries(unit, periodEnds, ArithmeticRollupMembers.Filter(members));
    }

    /// <summary>
    /// The derived margin axis: join the revenue and operating-income segment series by
    /// issuer member QName (under the fold), exact duration, and unit — never by display
    /// label or row position. Reported figures only: a missing leg or a non-positive
    /// revenue denominator yields no cell, and members/periods with no computable cell
    /// drop out entirely.
    /// </summary>
    public static AxisSeries BuildSegmentMarginSeries(AxisSeries revenue, AxisSeries income)
    {
        if (
            revenue.Members.Count == 0
            || income.Members.Count == 0
            || !string.Equals(revenue.Unit, income.Unit, StringComparison.Ordinal)
        )
        {
            return new AxisSeries("%", [], []);
        }

        var incomeByMember = income
            .Members.GroupBy(m => XbrlMemberNames.Fold(m.Member), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var incomePeriodIndex = income
            .PeriodEnds.Select((periodEnd, index) => (periodEnd, index))
            .ToDictionary(p => p.periodEnd, p => p.index);

        var members = new List<AxisMemberSeries>();
        var usedPeriods = new bool[revenue.PeriodEnds.Count];
        foreach (var revenueMember in revenue.Members)
        {
            if (
                !incomeByMember.TryGetValue(
                    XbrlMemberNames.Fold(revenueMember.Member),
                    out var incomeMember
                )
            )
            {
                continue;
            }

            var values = new List<decimal?>();
            var any = false;
            for (var i = 0; i < revenue.PeriodEnds.Count; i++)
            {
                decimal? margin = null;
                if (
                    incomePeriodIndex.TryGetValue(revenue.PeriodEnds[i], out var incomeIndex)
                    && revenueMember.PeriodStartAt(i) == incomeMember.PeriodStartAt(incomeIndex)
                    && revenueMember.Values[i] is { } revenueValue
                    && revenueValue > 0m
                    && incomeMember.Values[incomeIndex] is { } incomeValue
                )
                {
                    margin = incomeValue / revenueValue * 100m;
                    any = true;
                    usedPeriods[i] = true;
                }
                values.Add(margin);
            }

            if (any)
            {
                members.Add(
                    new AxisMemberSeries(revenueMember.Member, revenueMember.Label, values)
                );
            }
        }

        if (members.Count == 0)
        {
            return new AxisSeries("%", [], []);
        }

        var keptIndexes = Enumerable
            .Range(0, revenue.PeriodEnds.Count)
            .Where(i => usedPeriods[i])
            .ToList();
        return new AxisSeries(
            "%",
            keptIndexes.Select(i => revenue.PeriodEnds[i]).ToList(),
            members
                .Select(m => new AxisMemberSeries(
                    m.Member,
                    m.Label,
                    keptIndexes.Select(i => m.Values[i]).ToList(),
                    keptIndexes.Select(i => m.PeriodStartAt(i)).ToList()
                ))
                .ToList()
        );
    }

    /// <summary>
    /// Display label from the XBRL member QName: ISO country members get their English
    /// name; everything else drops the Member suffix and spaces the PascalCase local
    /// name.
    /// </summary>
    public static string Humanize(string memberQName)
    {
        var colon = memberQName.IndexOf(':');
        var prefix = colon > 0 ? memberQName[..colon] : "";
        var local = colon > 0 ? memberQName[(colon + 1)..] : memberQName;

        if (prefix == "country")
        {
            try
            {
                return new RegionInfo(local).EnglishName;
            }
            catch (ArgumentException)
            {
                return local;
            }
        }

        if (local.EndsWith("Member", StringComparison.Ordinal) && local.Length > "Member".Length)
        {
            local = local[..^"Member".Length];
        }
        // The (?<!^[A-Z]) guard keeps a lone leading capital attached to the word
        // that follows: Apple's IPhoneMember reads "IPhone", never "I Phone".
        // Boundaries deeper in the name still split ("USSegment" → "US Segment").
        return Regex.Replace(
            local,
            "(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?<!^[A-Z])(?=[A-Z][a-z])",
            " "
        );
    }
}
