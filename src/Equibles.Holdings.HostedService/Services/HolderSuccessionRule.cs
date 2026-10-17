using Equibles.CommonStocks.Data.Helpers;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Finds filers whose 13F reports continue under another CIK. A predecessor files no 13F after
/// quarter Q, a successor's first 13F is Q+1, and a filing ties them by identifier: a later 13F
/// notice from the predecessor, the predecessor's last included managers, the successor's first
/// included managers, or a shared identity number. Names are never compared.
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
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
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

            reports.TryGetValue(
                (predecessor.HolderId, predecessor.LastReportDate),
                out var lastReport
            );
            var lastIncluded = lastReport?.Included ?? [];
            foreach (var manager in lastIncluded)
            foreach (var successor in index.Filers(Keys(manager)))
                Add(
                    successor,
                    HolderSuccessionBasis.IncludedInLastReport,
                    lastReport.AccessionNumber
                );

            var ownKeys = Keys(predecessor).ToList();
            foreach (var successor in index.Including(ownKeys))
                Add(
                    successor,
                    HolderSuccessionBasis.SuccessorIncludesPredecessor,
                    index.Accession(successor)
                );

            if (lastIncluded.Count >= MinimumSharedManagers)
                foreach (var successor in index.Including(Keys(lastIncluded[0])))
                    if (
                        lastIncluded.All(manager =>
                            Keys(manager).Any(index.IncludedKeys(successor).Contains)
                        )
                    )
                        Add(
                            successor,
                            HolderSuccessionBasis.SameIncludedManagers,
                            index.Accession(successor)
                        );

            foreach (var successor in index.Filers(ownKeys.Where(key => !key.StartsWith("c:"))))
                Add(successor, HolderSuccessionBasis.SameIdentityNumber, null);
        }

        return found.Values.ToList();
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
        private readonly Dictionary<Guid, HashSet<string>> _includedKeys = [];
        private readonly Dictionary<Guid, string> _accessions = [];

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
                var keys = new HashSet<string>(StringComparer.Ordinal);
                if (reports.TryGetValue((filer.HolderId, quarter), out var report))
                {
                    _accessions[filer.HolderId] = report.AccessionNumber;
                    keys.UnionWith(report.Included.SelectMany(Keys));
                }
                _includedKeys[filer.HolderId] = keys;
                foreach (var key in keys)
                    Append(_byIncludedKey, key, filer);
            }
        }

        internal IEnumerable<SuccessionFiler> Filers(IEnumerable<string> keys) =>
            Lookup(_byKey, keys);

        internal IEnumerable<SuccessionFiler> Including(IEnumerable<string> keys) =>
            Lookup(_byIncludedKey, keys);

        internal HashSet<string> IncludedKeys(SuccessionFiler filer) =>
            _includedKeys[filer.HolderId];

        internal string Accession(SuccessionFiler filer) =>
            _accessions.GetValueOrDefault(filer.HolderId);

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
