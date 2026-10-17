using Equibles.CommonStocks.Data.Helpers;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Finds filers whose 13F reports continue under another CIK: a predecessor files no 13F after
/// quarter Q, a successor's first 13F is Q+1, and a filing ties them by identifier, never by name.
/// Each pair is stamped with its group's book values (<see cref="HoldingsSuccessionGroups"/>).
/// </summary>
internal static class HolderSuccessionRule
{
    /// <summary>Stamped on every succession; bump when the rules change.</summary>
    internal const int Version = 1;

    // Fewer shared managers than this is too thin to call two holding companies the same.
    internal const int MinimumSharedManagers = 3;

    internal static List<HolderSuccession> Derive(
        IReadOnlyCollection<SuccessionFiler> filers,
        IReadOnlyDictionary<(Guid HolderId, DateOnly ReportDate), SuccessionReport> reports,
        IReadOnlyCollection<SuccessionNotice> notices,
        DateOnly latestClosedQuarter
    )
    {
        var noticesByCik = notices
            .Where(notice => CikNormalizer.Canonicalize(notice.FilerCik) != null)
            .GroupBy(notice => CikNormalizer.Canonicalize(notice.FilerCik), StringComparer.Ordinal)
            // Earliest period first, so a pair keeps the first notice that names it.
            .ToDictionary(
                group => group.Key,
                group =>
                    group
                        .OrderBy(notice => notice.ReportDate)
                        .ThenBy(notice => notice.AccessionNumber, StringComparer.Ordinal)
                        .ToList(),
                StringComparer.Ordinal
            );
        var startedByQuarter = filers
            .GroupBy(filer => filer.FirstReportDate)
            .ToDictionary(group => group.Key, group => group.ToList());
        var indexes = new Dictionary<DateOnly, QuarterIndex>();
        var found = new Dictionary<(Guid, Guid), HolderSuccession>();

        foreach (var predecessor in filers)
        {
            // A successor's first quarter must be complete, or every late filer looks stopped.
            var next = HoldingsCorpusCoverage.FirstQuarterEndOnOrAfter(
                predecessor.LastReportDate.AddDays(1)
            );
            if (next > latestClosedQuarter || !startedByQuarter.TryGetValue(next, out var started))
                continue;
            if (!indexes.TryGetValue(next, out var index))
                indexes[next] = index = new QuarterIndex(started, next, reports);

            void Add(SuccessionFiler successor, HolderSuccessionBasis basis, string accession)
            {
                if (successor.HolderId == predecessor.HolderId)
                    return;
                var key = (predecessor.HolderId, successor.HolderId);
                if (!found.TryGetValue(key, out var succession))
                    found[key] = succession = new HolderSuccession
                    {
                        PredecessorHolderId = predecessor.HolderId,
                        SuccessorHolderId = successor.HolderId,
                        PredecessorLastReportDate = predecessor.LastReportDate,
                        SuccessorFirstReportDate = next,
                        DerivationVersion = Version,
                    };
                succession.Basis |= basis;
                succession.EvidenceAccession ??= accession;
            }

            var cik = CikNormalizer.Canonicalize(predecessor.Cik);
            if (cik != null && noticesByCik.TryGetValue(cik, out var filedNotices))
                foreach (var notice in filedNotices.Where(notice => notice.ReportDate >= next))
                foreach (var reporter in notice.Reporters)
                foreach (var successor in index.Filers(Keys(reporter)))
                    Add(successor, HolderSuccessionBasis.NoticeReportedBy, notice.AccessionNumber);

            var lastIncluded =
                reports
                    .GetValueOrDefault((predecessor.HolderId, predecessor.LastReportDate))
                    ?.Included
                ?? [];
            foreach (var included in lastIncluded)
            foreach (var successor in index.Filers(Keys(included.Manager)))
                Add(
                    successor,
                    HolderSuccessionBasis.IncludedInLastReport,
                    included.AccessionNumber
                );

            var ownKeys = Keys(predecessor).ToList();
            foreach (var successor in index.Including(ownKeys))
                Add(
                    successor,
                    HolderSuccessionBasis.SuccessorIncludesPredecessor,
                    index.Accession(successor, ownKeys)
                );

            var managers = DistinctManagers(lastIncluded);
            if (managers.Count >= MinimumSharedManagers)
                foreach (var successor in index.Including(managers[0]))
                    if (managers.All(keys => keys.Any(index.IncludedKeys(successor).ContainsKey)))
                        Add(
                            successor,
                            HolderSuccessionBasis.SameIncludedManagers,
                            index.Accession(successor, managers[0])
                        );

            // A shared CIK is a filer stored under two spellings of it.
            foreach (var successor in index.Filers(ownKeys))
                Add(successor, HolderSuccessionBasis.SameIdentityNumber, null);
        }

        var successions = found.Values.ToList();
        foreach (
            var group in HoldingsSuccessionGroups.Connect(
                successions,
                succession => succession.SuccessorFirstReportDate,
                succession => succession.PredecessorHolderId,
                succession => succession.SuccessorHolderId
            )
        )
        {
            var predecessorValue = group
                .DistinctBy(succession => succession.PredecessorHolderId)
                .Sum(succession =>
                    reports
                        .GetValueOrDefault(
                            (succession.PredecessorHolderId, succession.PredecessorLastReportDate)
                        )
                        ?.TotalValue
                    ?? 0
                );
            var successorValue = group
                .DistinctBy(succession => succession.SuccessorHolderId)
                .Sum(succession =>
                    reports
                        .GetValueOrDefault(
                            (succession.SuccessorHolderId, succession.SuccessorFirstReportDate)
                        )
                        ?.TotalValue
                    ?? 0
                );
            foreach (var succession in group)
            {
                succession.PredecessorGroupValue = predecessorValue;
                succession.SuccessorGroupValue = successorValue;
            }
        }
        return successions;
    }

    // The included managers filed with an identifier, one entry per manager however often it is
    // listed: rows sharing any identifier are the same manager.
    private static List<HashSet<string>> DistinctManagers(IEnumerable<IncludedManager> included)
    {
        var managers = new List<HashSet<string>>();
        foreach (var manager in included)
        {
            var keys = Keys(manager.Manager).ToHashSet(StringComparer.Ordinal);
            if (keys.Count == 0)
                continue;
            foreach (var same in managers.Where(keys.Overlaps).ToList())
            {
                keys.UnionWith(same);
                managers.Remove(same);
            }
            managers.Add(keys);
        }
        return managers;
    }

    // Each filed identifier as a typed key, so a CIK never matches a CRD with the same digits.
    internal static IEnumerable<string> Keys(ManagerReference manager) =>
        Keys(manager.Cik, manager.Form13FFileNumber, manager.CrdNumber, manager.SecFileNumber);

    private static IEnumerable<string> Keys(SuccessionFiler filer) =>
        Keys(filer.Cik, filer.Form13FFileNumber, filer.CrdNumber, filer.SecFileNumber);

    private static IEnumerable<string> Keys(
        string cik,
        string form13FFileNumber,
        string crdNumber,
        string secFileNumber
    )
    {
        if (CikNormalizer.Canonicalize(cik) is { } canonicalCik)
            yield return "c:" + canonicalCik;
        if (HolderIdentifierRules.NormalizeForm13FFileNumber(form13FFileNumber) is { } fileNumber)
            yield return "f:" + fileNumber;
        if (HolderIdentifierRules.ParseCrd(crdNumber) is { } crd)
            yield return "r:" + crd;
        if (HolderIdentifierRules.NormalizeSecFileNumber(secFileNumber) is { } secNumber)
            yield return "s:" + secNumber;
    }

    /// <summary>The filers that started in one quarter, indexed by their own and their included managers' keys.</summary>
    private sealed class QuarterIndex
    {
        private readonly Dictionary<string, List<SuccessionFiler>> _byKey = new(
            StringComparer.Ordinal
        );
        private readonly Dictionary<string, List<SuccessionFiler>> _byIncludedKey = new(
            StringComparer.Ordinal
        );

        // Each started filer's included keys, with the earliest filing that listed each.
        private readonly Dictionary<Guid, Dictionary<string, string>> _includedKeys = [];

        internal QuarterIndex(
            IEnumerable<SuccessionFiler> started,
            DateOnly quarter,
            IReadOnlyDictionary<(Guid HolderId, DateOnly ReportDate), SuccessionReport> reports
        )
        {
            foreach (var filer in started)
            {
                foreach (var key in Keys(filer))
                    Append(_byKey, key, filer);
                var keys = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (
                    var included in reports.GetValueOrDefault((filer.HolderId, quarter))?.Included
                        ?? []
                )
                foreach (var key in Keys(included.Manager))
                    keys.TryAdd(key, included.AccessionNumber);
                _includedKeys[filer.HolderId] = keys;
                foreach (var key in keys.Keys)
                    Append(_byIncludedKey, key, filer);
            }
        }

        internal IEnumerable<SuccessionFiler> Filers(IEnumerable<string> keys) =>
            Lookup(_byKey, keys);

        internal IEnumerable<SuccessionFiler> Including(IEnumerable<string> keys) =>
            Lookup(_byIncludedKey, keys);

        internal IReadOnlyDictionary<string, string> IncludedKeys(SuccessionFiler filer) =>
            _includedKeys[filer.HolderId];

        // The filing that listed the first of these keys the filer included.
        internal string Accession(SuccessionFiler filer, IEnumerable<string> keys) =>
            keys.Select(key => _includedKeys[filer.HolderId].GetValueOrDefault(key))
                .FirstOrDefault(accession => accession != null);

        private static IEnumerable<SuccessionFiler> Lookup(
            Dictionary<string, List<SuccessionFiler>> index,
            IEnumerable<string> keys
        ) =>
            keys.SelectMany(key => index.GetValueOrDefault(key) ?? [])
                .DistinctBy(filer => filer.HolderId);

        private static void Append(
            Dictionary<string, List<SuccessionFiler>> index,
            string key,
            SuccessionFiler filer
        )
        {
            if (!index.TryGetValue(key, out var list))
                index[key] = list = [];
            list.Add(filer);
        }
    }
}
