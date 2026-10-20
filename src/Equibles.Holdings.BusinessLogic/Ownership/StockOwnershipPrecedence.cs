using Equibles.Holdings.BusinessLogic.Ownership.Models;
using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.BusinessLogic.Ownership;

/// <summary>
/// Arranges every lane's rows into the default view: one top row per holder from its freshest
/// filing, the holder's other filings and the disclosures its 13F report contains nested under it.
/// </summary>
/// <remarks>
/// A holder is its CIK, so a filer's 13F, 13D, 13G and Forms 3 to 5 are one holder. The freshest
/// as-of date wins (13F report date, 13D or 13G event date, insider transaction date), then the
/// filing date, then 13F before 13D or 13G before insider. A holder without a 13F row of its own
/// nests under the 13F filer its confirmed reporting rollup names, unless it filed its own 13F
/// for that filer's quarter; a fund series nests under its adviser's 13F filer. Nesting never
/// reads names, and two 13F filers never nest, since 13F reports are disjoint.
/// </remarks>
public static class StockOwnershipPrecedence
{
    /// <summary>Insider filings older than this are stale.</summary>
    public const int InsiderStaleMonths = 24;

    public sealed class Context
    {
        /// <summary>Stored rollups keyed by canonical CIK; a CIK without one rolls up to itself.</summary>
        public IReadOnlyDictionary<string, HolderRollup> Rollups { get; init; } =
            new Dictionary<string, HolderRollup>();

        /// <summary>
        /// Whether the CIK filed its own 13F report (of any securities) for the quarter, which
        /// makes it its own holder whatever an earlier quarter's reporting evidence says.
        /// </summary>
        public Func<string, DateOnly, bool> FiledOwn13F { get; init; } = (_, _) => false;

        public DateOnly Today { get; init; }
    }

    /// <summary>
    /// Flags stale rows, sets each row's parent and covering 13F filer, and returns every top row
    /// with its children, largest first. Hidden (stale) rows stay in the result with their flag.
    /// </summary>
    public static List<StockHolderRow> Arrange(IReadOnlyList<StockHolderRow> rows, Context context)
    {
        foreach (var row in rows)
        {
            row.ParentRowKey = null;
            row.Covering13FCik = null;
            row.Children.Clear();
            if (row.Shares <= 0)
                row.Flags |= StockHolderRowFlags.Stale;
            if (
                row.Source == StockHolderSource.Insider
                && row.AsOf < context.Today.AddMonths(-InsiderStaleMonths)
            )
                row.Flags |= StockHolderRowFlags.Stale;
        }

        // One group per holder: a CIK joins every source; any other key stands alone.
        var groups = rows.GroupBy(row =>
                row.HolderKey.Kind == StockHolderKeyKind.Cik ? row.HolderKey.ToString() : row.RowKey
            )
            .Select(group => new HolderGroup(group.ToList()))
            .ToList();
        var groupsBy13FCik = groups
            .Where(group => group.Form13F != null && group.Form13F.Cik != null)
            .ToDictionary(group => group.Form13F.Cik, StringComparer.Ordinal);
        var groupsBy13FHolder = groups
            .Where(group => group.Form13F?.InstitutionalHolderId != null)
            .ToDictionary(group => group.Form13F.InstitutionalHolderId!.Value);

        foreach (var group in groups)
        {
            if (group.Form13F != null)
            {
                foreach (var row in group.Rows)
                    row.Covering13FCik = group.Form13F.Cik;
                MarkOlderSchedulesStale(group.Rows, group.Form13F.AsOf);
                continue;
            }
            var parent = ParentFor(group, groupsBy13FCik, groupsBy13FHolder, context);
            if (parent == null)
                continue;
            group.Parent = parent;
            foreach (var row in group.Rows)
                row.Covering13FCik = parent.Form13F.Cik;
            MarkOlderSchedulesStale(group.Rows, parent.Form13F.AsOf);
        }

        var tops = new List<StockHolderRow>();
        foreach (var group in groups)
        {
            var top = (group.Parent ?? group).Top;
            foreach (var row in group.Rows.Where(row => row != top))
            {
                row.ParentRowKey = top.RowKey;
                top.Children.Add(row);
            }
            if (group.Parent == null)
                tops.Add(top);
        }
        foreach (var top in tops)
        {
            var ordered = Ordered(top.Children).ToList();
            top.Children.Clear();
            top.Children.AddRange(ordered);
            top.ChildCount = ordered.Count;
        }
        return Ordered(tops).ToList();
    }

    /// <summary>The rows the default view shows: visible top rows, each with its visible children.</summary>
    public static List<StockHolderRow> Visible(IEnumerable<StockHolderRow> tops) =>
        tops.Where(top => !top.Has(StockHolderRowFlags.Stale))
            .Select(top =>
            {
                top.Children.RemoveAll(child => child.Has(StockHolderRowFlags.Stale));
                top.ChildCount = top.Children.Count;
                return top;
            })
            .ToList();

    public static IEnumerable<StockHolderRow> Ordered(IEnumerable<StockHolderRow> rows) =>
        rows.OrderByDescending(row => row.Shares)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.RowKey, StringComparer.Ordinal);

    // A 13D or 13G older than the 13F quarter that covers its holder is already inside that report.
    private static void MarkOlderSchedulesStale(IEnumerable<StockHolderRow> rows, DateOnly quarter)
    {
        foreach (var row in rows.Where(row => IsSchedule(row.Source) && row.AsOf < quarter))
            row.Flags |= StockHolderRowFlags.Stale;
    }

    private static HolderGroup ParentFor(
        HolderGroup group,
        IReadOnlyDictionary<string, HolderGroup> groupsBy13FCik,
        IReadOnlyDictionary<Guid, HolderGroup> groupsBy13FHolder,
        Context context
    )
    {
        var top = group.Top;
        if (top.Source == StockHolderSource.Fund)
            return top.AdviserHolderId is { } adviser
                ? groupsBy13FHolder.GetValueOrDefault(adviser)
                : null;
        if (top.Cik == null || !context.Rollups.TryGetValue(top.Cik, out var rollup))
            return null;
        var parent = ReportingParentCandidates(rollup)
            .Where(candidate => candidate != top.Cik)
            .Select(candidate => groupsBy13FCik.GetValueOrDefault(candidate))
            .FirstOrDefault(candidateGroup => candidateGroup != null);
        if (parent == null || context.FiledOwn13F(top.Cik, parent.Form13F.AsOf))
            return null;
        return parent;
    }

    /// <summary>
    /// The 13F filers whose reports contain a holder's shares by its rollup: the direct reporting
    /// parent, then the top of the 13F reporting chain. A GLEIF parent states ownership, never that
    /// a report contains the holder, and conflicting evidence leaves the holder on its own.
    /// </summary>
    public static IEnumerable<string> ReportingParentCandidates(HolderRollup rollup)
    {
        if (rollup == null || rollup.HasConflictingEvidence)
            yield break;
        if (rollup.ReportingParentCik != null)
            yield return rollup.ReportingParentCik;
        if (rollup.Basis == HolderRollupBasis.ReportingParent && rollup.UltimateCik != null)
            yield return rollup.UltimateCik;
    }

    private static bool IsSchedule(StockHolderSource source) =>
        source is StockHolderSource.Schedule13D or StockHolderSource.Schedule13G;

    private static int SourceRank(StockHolderSource source) =>
        source switch
        {
            StockHolderSource.Form13F => 0,
            StockHolderSource.Schedule13D or StockHolderSource.Schedule13G => 1,
            StockHolderSource.Insider => 2,
            _ => 3,
        };

    private sealed class HolderGroup
    {
        public HolderGroup(List<StockHolderRow> rows)
        {
            Rows = rows;
            Top = rows.OrderByDescending(row => row.AsOf)
                .ThenByDescending(row => row.FilingDate ?? DateOnly.MinValue)
                .ThenBy(row => SourceRank(row.Source))
                .ThenBy(row => row.RowKey, StringComparer.Ordinal)
                .First();
            Form13F = rows.FirstOrDefault(row => row.Source == StockHolderSource.Form13F);
        }

        public List<StockHolderRow> Rows { get; }
        public StockHolderRow Top { get; }
        public StockHolderRow Form13F { get; }
        public HolderGroup Parent { get; set; }
    }
}
