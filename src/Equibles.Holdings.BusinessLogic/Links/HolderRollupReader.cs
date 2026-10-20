using Equibles.CommonStocks.Data.Helpers;
using Equibles.Core.AutoWiring;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Holdings.BusinessLogic.Links;

[Service(ServiceLifetime.Scoped, typeof(IHolderRollupReader))]
public class HolderRollupReader : IHolderRollupReader
{
    /// <summary>The kinds that say a child's holdings are inside the parent's 13F report.</summary>
    public static readonly IReadOnlyList<HolderLinkKind> ReportingKinds =
    [
        HolderLinkKind.IncludedInParentReport,
        HolderLinkKind.ChildCoverReportsBy,
        HolderLinkKind.NoticeReportedBy,
    ];

    private readonly InstitutionalHolderRepository _repository;

    public HolderRollupReader(InstitutionalHolderRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyDictionary<string, HolderRollup>> GetRollups(
        IEnumerable<string> ciks,
        CancellationToken cancellationToken = default
    )
    {
        var canonical = ciks.Select(CikNormalizer.Canonicalize)
            .Where(cik => cik != null)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (canonical.Count == 0)
            return new Dictionary<string, HolderRollup>(StringComparer.Ordinal);
        return await _repository
            .GetRollups(canonical)
            .AsNoTracking()
            .ToDictionaryAsync(rollup => rollup.Cik, StringComparer.Ordinal, cancellationToken);
    }

    public async Task<IReadOnlyList<HolderLinkEvidence>> GetContainedChildren(
        InstitutionalHolder holder,
        DateOnly quarter,
        CancellationToken cancellationToken = default
    )
    {
        if (CikNormalizer.Canonicalize(holder?.Cik) is not { } parentCik)
            return [];
        var quarterEnd = HoldingsCorpusCoverage.FirstQuarterEndOnOrAfter(quarter);
        DateOnly[] reportDates =
        [
            quarterEnd,
            HoldingsCorpusCoverage.PreviousQuarterEnd(quarterEnd),
        ];
        return await _repository
            .GetLinkEvidenceByParent(parentCik, ReportingKinds.ToList(), reportDates)
            .AsNoTracking()
            .OrderBy(evidence => evidence.ChildCik)
            .ThenByDescending(evidence => evidence.ReportDate)
            .ThenBy(evidence => evidence.Kind)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, FundSeriesHolderLink>> GetFundSeriesLinks(
        IEnumerable<string> seriesIds,
        CancellationToken cancellationToken = default
    )
    {
        var wanted = seriesIds
            .Where(seriesId => !string.IsNullOrWhiteSpace(seriesId))
            .Select(seriesId => seriesId.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (wanted.Count == 0)
            return new Dictionary<string, FundSeriesHolderLink>(StringComparer.Ordinal);
        return await _repository
            .GetFundSeriesLinks(wanted)
            .AsNoTracking()
            .ToDictionaryAsync(link => link.SeriesId, StringComparer.Ordinal, cancellationToken);
    }
}
