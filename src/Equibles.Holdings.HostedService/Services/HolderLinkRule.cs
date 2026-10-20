using Equibles.CommonStocks.Data.Helpers;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Derives holder parent links from filed identifiers only: 13F other-manager lists and notices of
/// the recent closed quarters, GLEIF parents from an optional source, and current N-CEN advisers.
/// A manager resolves by CIK, then 13F file number, then CRD; a name alone never links, no holder
/// is invented, and a succession pair is never a parent.
/// </summary>
internal static class HolderLinkRule
{
    /// <summary>Stamped on every rollup; bump when the rules change.</summary>
    internal const int Version = 1;

    /// <summary>A parent chain stops after this many parents.</summary>
    internal const int MaxDepth = 4;

    internal static HolderLinkSet Derive(
        IReadOnlyCollection<LinkHolder> holders,
        IReadOnlyDictionary<Guid, DateOnly> newest13FReports,
        IReadOnlyCollection<LinkManagerRow> managers,
        IReadOnlyCollection<LinkNotice> notices,
        IReadOnlyCollection<(Guid PredecessorHolderId, Guid SuccessorHolderId)> successions,
        IReadOnlyCollection<HolderLinkEvidence> sourced,
        IReadOnlyCollection<SeriesAdviserRow> seriesAdvisers
    )
    {
        var holderCiks = new Dictionary<Guid, string>();
        var holderIdsByCik = new Dictionary<string, List<Guid>>(StringComparer.Ordinal);
        foreach (var holder in holders)
        {
            if (CikNormalizer.Canonicalize(holder.Cik) is not { } cik)
                continue;
            holderCiks[holder.HolderId] = cik;
            if (!holderIdsByCik.TryGetValue(cik, out var ids))
                holderIdsByCik[cik] = ids = [];
            ids.Add(holder.HolderId);
        }
        // Two holders stored under spellings of one CIK are not told apart here.
        Guid? HolderId(string cik) =>
            cik != null && holderIdsByCik.TryGetValue(cik, out var ids) && ids.Count == 1
                ? ids[0]
                : null;

        // A notice amended for the same quarter restates the first, so the latest filed one speaks.
        var latestNotices = notices
            .Where(notice => CikNormalizer.Canonicalize(notice.FilerCik) != null)
            .GroupBy(notice => (CikNormalizer.Canonicalize(notice.FilerCik), notice.ReportDate))
            .Select(group =>
                group
                    .OrderByDescending(notice => notice.FilingDate)
                    .ThenByDescending(notice => notice.AccessionNumber, StringComparer.Ordinal)
                    .First()
            )
            .ToList();
        var index = new IdentifierIndex(holders, latestNotices, managers);

        var successionPairs = new HashSet<(string, string)>();
        foreach (var (predecessor, successor) in successions)
        {
            if (
                holderCiks.TryGetValue(predecessor, out var predecessorCik)
                && holderCiks.TryGetValue(successor, out var successorCik)
            )
            {
                successionPairs.Add((predecessorCik, successorCik));
                successionPairs.Add((successorCik, predecessorCik));
            }
        }

        var evidence =
            new Dictionary<(string, string, HolderLinkKind, string), HolderLinkEvidence>();
        void Add(
            string child,
            string parent,
            HolderLinkKind kind,
            string sourceKey,
            DateOnly? reportDate,
            DateOnly asOf,
            string childLei = null,
            string parentLei = null
        )
        {
            if (child == null || string.IsNullOrWhiteSpace(sourceKey))
                return;
            if (
                parent == null
                    ? !IsGleif(kind)
                    : parent == child || successionPairs.Contains((child, parent))
            )
                return;
            evidence.TryAdd(
                (child, parent, kind, sourceKey),
                new HolderLinkEvidence
                {
                    ChildCik = child,
                    ChildHolderId = HolderId(child),
                    ParentCik = parent,
                    ParentHolderId = HolderId(parent),
                    ChildLei = childLei,
                    ParentLei = parentLei,
                    Kind = kind,
                    SourceKey = sourceKey,
                    ReportDate = reportDate,
                    AsOf = asOf,
                }
            );
        }

        foreach (var row in managers)
        {
            if (!holderCiks.TryGetValue(row.FilerHolderId, out var filer))
                continue;
            var manager = index.Resolve(row.Manager);
            if (row.Direction == OtherManagerDirection.IncludedInReport)
                Add(
                    manager,
                    filer,
                    HolderLinkKind.IncludedInParentReport,
                    row.AccessionNumber,
                    row.ReportDate,
                    row.FilingDate
                );
            else
                Add(
                    filer,
                    manager,
                    HolderLinkKind.ChildCoverReportsBy,
                    row.AccessionNumber,
                    row.ReportDate,
                    row.FilingDate
                );
        }
        foreach (var notice in latestNotices)
        {
            var filer = CikNormalizer.Canonicalize(notice.FilerCik);
            foreach (var reporter in notice.Reporters)
                Add(
                    filer,
                    index.Resolve(reporter),
                    HolderLinkKind.NoticeReportedBy,
                    notice.AccessionNumber,
                    notice.ReportDate,
                    notice.FilingDate
                );
        }
        foreach (var row in sourced.Where(row => IsGleif(row.Kind)))
            Add(
                CikNormalizer.Canonicalize(row.ChildCik),
                CikNormalizer.Canonicalize(row.ParentCik),
                row.Kind,
                row.SourceKey ?? row.ChildLei,
                null,
                row.AsOf,
                row.ChildLei,
                row.ParentLei
            );

        var stored = evidence.Values.ToList();
        // The raw count, so a notice naming one manager with an identifier and one without is not sole.
        var soleNotices = latestNotices
            .Where(notice => notice.Reporters.Count == 1)
            .Select(notice => notice.AccessionNumber)
            .ToHashSet(StringComparer.Ordinal);
        // The quarter of each filer's newest own 13F report or notice in the window; its parent is
        // read there alone, so an older notice never outlives a later report of its own.
        var newestOwnFiling = new Dictionary<string, DateOnly>(StringComparer.Ordinal);
        void Filed(string cik, DateOnly quarter)
        {
            if (
                cik != null
                && (!newestOwnFiling.TryGetValue(cik, out var newest) || quarter > newest)
            )
                newestOwnFiling[cik] = quarter;
        }
        foreach (var (holderId, quarter) in newest13FReports)
            Filed(holderCiks.GetValueOrDefault(holderId), quarter);
        foreach (var notice in latestNotices)
            Filed(CikNormalizer.Canonicalize(notice.FilerCik), notice.ReportDate);
        var reporting = ReportingParents(stored, soleNotices, newestOwnFiling);
        var gleifUltimate = GleifParents(stored, HolderLinkKind.GleifUltimateParent);
        var gleifDirect = GleifParents(stored, HolderLinkKind.GleifDirectParent);

        var rollups = holderCiks
            .Values.Concat(stored.Select(row => row.ChildCik))
            .Concat(stored.Where(row => row.ParentCik != null).Select(row => row.ParentCik))
            .Distinct(StringComparer.Ordinal)
            .Select(cik => Rollup(cik, reporting, gleifUltimate, gleifDirect, HolderId))
            .ToList();
        var fundLinks = FundSeriesLinks(holders, seriesAdvisers, index, reporting, HolderId);
        return new HolderLinkSet(stored, rollups, fundLinks);
    }

    private static bool IsGleif(HolderLinkKind kind) =>
        kind is HolderLinkKind.GleifDirectParent or HolderLinkKind.GleifUltimateParent;

    // Each child's 13F reporting parent in the quarter of its newest own report or notice: a
    // notice naming exactly one manager, or an included edge the child's cover or notice confirms.
    private static Dictionary<string, Decision> ReportingParents(
        IReadOnlyCollection<HolderLinkEvidence> evidence,
        IReadOnlySet<string> soleNotices,
        IReadOnlyDictionary<string, DateOnly> newestOwnFiling
    )
    {
        var decisions = new Dictionary<string, Decision>(StringComparer.Ordinal);
        foreach (
            var child in evidence
                .Where(row => row.ReportDate != null && !IsGleif(row.Kind))
                .GroupBy(row => row.ChildCik, StringComparer.Ordinal)
        )
        {
            if (!newestOwnFiling.TryGetValue(child.Key, out var quarter))
                continue;
            var rows = child.Where(row => row.ReportDate == quarter).ToList();
            var confirming = rows.Where(row =>
                    row.Kind
                        is HolderLinkKind.ChildCoverReportsBy
                            or HolderLinkKind.NoticeReportedBy
                )
                .Select(row => row.ParentCik)
                .ToHashSet(StringComparer.Ordinal);
            var candidates = rows.Where(row =>
                    (
                        row.Kind == HolderLinkKind.NoticeReportedBy
                        && soleNotices.Contains(row.SourceKey)
                    )
                    || (
                        row.Kind == HolderLinkKind.IncludedInParentReport
                        && confirming.Contains(row.ParentCik)
                    )
                )
                .Select(row => row.ParentCik)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (candidates.Count > 0)
                decisions[child.Key] =
                    candidates.Count == 1
                        ? new Decision(candidates[0], false)
                        : new Decision(null, true);
        }
        return decisions;
    }

    // GLEIF records are current, so every resolved parent of one kind competes at once; a parent
    // LEI that maps to no CIK cannot be grouped under and does not decide.
    private static Dictionary<string, Decision> GleifParents(
        IReadOnlyCollection<HolderLinkEvidence> evidence,
        HolderLinkKind kind
    ) =>
        evidence
            .Where(row => row.Kind == kind && row.ParentCik != null)
            .GroupBy(row => row.ChildCik, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var parents = group
                        .Select(row => row.ParentCik)
                        .Distinct(StringComparer.Ordinal)
                        .ToList();
                    return parents.Count == 1
                        ? new Decision(parents[0], false)
                        : new Decision(null, true);
                },
                StringComparer.Ordinal
            );

    private static HolderRollup Rollup(
        string cik,
        IReadOnlyDictionary<string, Decision> reporting,
        IReadOnlyDictionary<string, Decision> gleifUltimate,
        IReadOnlyDictionary<string, Decision> gleifDirect,
        Func<string, Guid?> holderId
    )
    {
        var reportingParent = reporting.GetValueOrDefault(cik)?.Parent;
        var (ultimate, basis, conflict) = (cik, HolderRollupBasis.Self, false);
        if (gleifUltimate.TryGetValue(cik, out var ultimateDecision))
        {
            if (ultimateDecision.Conflict)
                conflict = true;
            else
                (ultimate, basis) = (
                    ultimateDecision.Parent,
                    HolderRollupBasis.GleifUltimateParent
                );
        }
        else if (gleifDirect.ContainsKey(cik))
            (ultimate, basis, conflict) = Chain(
                cik,
                gleifDirect,
                HolderRollupBasis.GleifDirectParent
            );
        else if (reporting.ContainsKey(cik))
            (ultimate, basis, conflict) = Chain(cik, reporting, HolderRollupBasis.ReportingParent);

        return new HolderRollup
        {
            Cik = cik,
            ReportingParentCik = reportingParent,
            UltimateCik = ultimate,
            UltimateHolderId = holderId(ultimate),
            Basis = ultimate == cik ? HolderRollupBasis.Self : basis,
            HasConflictingEvidence = conflict,
            DerivationVersion = Version,
        };
    }

    // Follows one tier's parents from the start, which rolls to itself on a conflict or a cycle.
    private static (string Top, HolderRollupBasis Basis, bool Conflict) Chain(
        string start,
        IReadOnlyDictionary<string, Decision> parents,
        HolderRollupBasis basis
    )
    {
        if (parents[start].Conflict)
            return (start, HolderRollupBasis.Self, true);
        var top = Top(start, parents);
        return top == null ? (start, HolderRollupBasis.Self, true) : (top, basis, false);
    }

    // The last parent reached within MaxDepth, the start itself without one, or null on a cycle.
    private static string Top(string start, IReadOnlyDictionary<string, Decision> parents)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { start };
        var current = start;
        for (var depth = 0; depth < MaxDepth; depth++)
        {
            if (parents.GetValueOrDefault(current)?.Parent is not { } next)
                break;
            if (!seen.Add(next))
                return null;
            current = next;
        }
        return current;
    }

    // A series whose latest N-CEN names exactly one investment adviser CRD links to the 13F filer
    // that CRD identifies, followed up its 13F reporting chain to the filer that reports it.
    private static List<FundSeriesHolderLink> FundSeriesLinks(
        IReadOnlyCollection<LinkHolder> holders,
        IReadOnlyCollection<SeriesAdviserRow> seriesAdvisers,
        IdentifierIndex index,
        IReadOnlyDictionary<string, Decision> reporting,
        Func<string, Guid?> holderId
    )
    {
        var profileCiks = holders
            .Where(holder =>
                holder.AdviserCrd != null && CikNormalizer.Canonicalize(holder.Cik) != null
            )
            .GroupBy(holder => holder.AdviserCrd.Value)
            .ToDictionary(
                group => group.Key,
                group =>
                    group
                        .Select(holder => CikNormalizer.Canonicalize(holder.Cik))
                        .Distinct(StringComparer.Ordinal)
                        .ToList()
            );
        var links = new List<FundSeriesHolderLink>();
        foreach (
            var series in seriesAdvisers
                .Where(row => !string.IsNullOrWhiteSpace(row.SeriesId))
                .GroupBy(row => row.SeriesId.Trim(), StringComparer.Ordinal)
        )
        {
            var latest = series
                .OrderByDescending(row => row.ReportEndingPeriod)
                .ThenByDescending(row => row.FilingDate)
                .ThenByDescending(row => row.AccessionNumber, StringComparer.Ordinal)
                .First();
            var advisers = series
                .Where(row => row.AccessionNumber == latest.AccessionNumber)
                .ToList();
            var crds = advisers.Select(row => row.Crd).Distinct().ToList();
            if (crds is not [{ } crd])
                continue;

            string adviserCik;
            if (profileCiks.TryGetValue(crd, out var ciks))
            {
                if (ciks.Count != 1)
                    continue;
                adviserCik = ciks[0];
            }
            else if (index.ResolveCrd(crd) is { } filed)
                adviserCik = filed;
            else
                continue;

            var top = Top(adviserCik, reporting) ?? adviserCik;
            if (holderId(top) is not { } holder)
                continue;
            links.Add(
                new FundSeriesHolderLink
                {
                    SeriesId = series.Key,
                    RegistrantCik = CikNormalizer.Canonicalize(latest.RegistrantCik),
                    AdviserCrd = crd,
                    InstitutionalHolderId = holder,
                    Basis =
                        top == adviserCik
                            ? FundSeriesHolderLinkBasis.AdviserFiler
                            : FundSeriesHolderLinkBasis.ReportingParent,
                    NCenReportEndingPeriod = latest.ReportEndingPeriod,
                }
            );
        }
        return links;
    }

    private sealed record Decision(string Parent, bool Conflict);

    /// <summary>
    /// Maps a filed 13F file number or CRD to the one CIK it belongs to. A filer's own numbers (its
    /// 13F cover, its notice, its Form ADV profile) are authoritative; a number another filer lists
    /// beside a CIK counts only when no filer claims it. A number claimed by two CIKs is unresolved.
    /// </summary>
    private sealed class IdentifierIndex
    {
        private readonly Dictionary<string, HashSet<string>> _own = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> _listed = new(StringComparer.Ordinal);

        internal IdentifierIndex(
            IEnumerable<LinkHolder> holders,
            IEnumerable<LinkNotice> notices,
            IEnumerable<LinkManagerRow> managers
        )
        {
            foreach (var holder in holders)
            {
                var cik = CikNormalizer.Canonicalize(holder.Cik);
                Claim(_own, FileKey(holder.Form13FFileNumber), cik);
                Claim(_own, CrdKey(holder.CrdNumber), cik);
                if (holder.AdviserCrd is { } adviserCrd)
                    Claim(_own, CrdKey(adviserCrd), cik);
            }
            foreach (var notice in notices)
            {
                var cik = CikNormalizer.Canonicalize(notice.FilerCik);
                Claim(_own, FileKey(notice.Form13FFileNumber), cik);
                Claim(_own, CrdKey(notice.CrdNumber), cik);
                foreach (var reporter in notice.Reporters)
                    ClaimListed(reporter);
            }
            foreach (var manager in managers)
                ClaimListed(manager.Manager);
        }

        internal string Resolve(ManagerReference manager)
        {
            if (CikNormalizer.Canonicalize(manager.Cik) is { } cik)
                return cik;
            return Lookup(FileKey(manager.Form13FFileNumber)) ?? Lookup(CrdKey(manager.CrdNumber));
        }

        internal string ResolveCrd(int crd) => Lookup(CrdKey(crd));

        private string Lookup(string key)
        {
            if (key == null)
                return null;
            var claims = _own.GetValueOrDefault(key) ?? _listed.GetValueOrDefault(key);
            return claims is { Count: 1 } ? claims.First() : null;
        }

        private void ClaimListed(ManagerReference manager)
        {
            var cik = CikNormalizer.Canonicalize(manager.Cik);
            Claim(_listed, FileKey(manager.Form13FFileNumber), cik);
            Claim(_listed, CrdKey(manager.CrdNumber), cik);
        }

        private static void Claim(
            Dictionary<string, HashSet<string>> claims,
            string key,
            string cik
        )
        {
            if (key == null || cik == null)
                return;
            if (!claims.TryGetValue(key, out var ciks))
                claims[key] = ciks = new HashSet<string>(StringComparer.Ordinal);
            ciks.Add(cik);
        }

        private static string FileKey(string fileNumber) =>
            HolderIdentifierRules.NormalizeForm13FFileNumber(fileNumber) is { } normalized
                ? "f:" + normalized
                : null;

        private static string CrdKey(string crd) =>
            HolderIdentifierRules.ParseCrd(crd) is { } parsed ? CrdKey(parsed) : null;

        private static string CrdKey(int crd) => "r:" + crd;
    }
}
