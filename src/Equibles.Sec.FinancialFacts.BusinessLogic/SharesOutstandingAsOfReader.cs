using Equibles.Core.AutoWiring;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Statements;
using Equibles.Sec.FinancialFacts.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Sec.FinancialFacts.BusinessLogic;

/// <summary>
/// Every issuer's dei:EntityCommonStockSharesOutstanding cover-page count as of a past date, in
/// one read: the count stated for the latest date on or before it, no older than
/// <see cref="MaximumAgeDays"/>. Where the latest date carries per-class counts on a class-of-stock
/// axis, their sum is the entity total and is flagged as such.
/// </summary>
/// <remarks>
/// The as-of rule is the fact's own date (its PeriodEnd, the instant the cover page states), never
/// its fiscal stamp: a cover count is dated a few weeks after the period it reports. The count is
/// on the basis of that date; callers restate it across later splits from it. Unlike
/// <see cref="SharesOutstandingProvider"/>, which answers one issuer today, this answers a
/// quarter for all issuers and runs no collapse check, so callers label it an estimate.
/// </remarks>
[Service(ServiceLifetime.Scoped)]
public class SharesOutstandingAsOfReader
{
    /// <summary>A count stated more than about a year and a month before the date is stale.</summary>
    public const int MaximumAgeDays = 400;

    private const string SharesUnit = "shares";

    private static readonly DocumentType[] ForeignAnnualForms =
    [
        DocumentType.TwentyF,
        DocumentType.TwentyFa,
        DocumentType.FortyF,
        DocumentType.FortyFa,
    ];

    private readonly FinancialFactRepository _financialFactRepository;
    private readonly FinancialConceptRepository _financialConceptRepository;

    public SharesOutstandingAsOfReader(
        FinancialFactRepository financialFactRepository,
        FinancialConceptRepository financialConceptRepository
    )
    {
        _financialFactRepository = financialFactRepository;
        _financialConceptRepository = financialConceptRepository;
    }

    /// <summary>Each issuer's count as of <paramref name="asOf"/>; an issuer with none is absent.</summary>
    public async Task<Dictionary<Guid, SharesOutstandingAsOf>> Read(
        DateOnly asOf,
        CancellationToken cancellationToken = default
    )
    {
        if (!FinancialConceptAliases.TryResolve("shares-outstanding", out var refs))
            return [];
        var tags = refs.Where(r => r.Taxonomy == FactTaxonomy.Dei).Select(r => r.Tag).ToList();
        if (tags.Count == 0)
            return [];
        var conceptIds = await _financialConceptRepository
            .GetMatching([FactTaxonomy.Dei], tags)
            .Select(concept => concept.Id)
            .ToListAsync(cancellationToken);
        if (conceptIds.Count == 0)
            return [];

        var earliest = asOf.AddDays(-MaximumAgeDays);
        var facts = await _financialFactRepository
            .GetAll()
            .Where(fact =>
                conceptIds.Contains(fact.FinancialConceptId)
                && fact.Unit == SharesUnit
                && fact.PeriodEnd <= asOf
                && fact.PeriodEnd > earliest
            )
            .Where(CurrentShareEvidenceDates.Eligible)
            .Select(fact => new FactRow
            {
                Id = fact.Id,
                IssuerId = fact.EquityIssuerId,
                Value = fact.Value,
                PeriodEnd = fact.PeriodEnd,
                FiledDate = fact.FiledDate,
                Form = fact.Form,
                AccessionNumber = fact.AccessionNumber,
                Consolidated = fact.DimensionsKey == "",
            })
            .ToListAsync(cancellationToken);
        var dimensionalIds = facts.Where(f => !f.Consolidated).Select(f => f.Id).ToList();
        var classMembers = new Dictionary<Guid, (string Axis, string Member)>();
        foreach (var chunk in dimensionalIds.Chunk(5_000))
        {
            var dimensions = await _financialFactRepository
                .GetAll()
                .Where(fact => chunk.Contains(fact.Id) && fact.Dimensions.Count == 1)
                .SelectMany(fact => fact.Dimensions)
                .Where(dimension =>
                    SharesOutstandingProvider.ClassOfStockAxes.Contains(dimension.Axis)
                    && !SharesOutstandingProvider.NonClassMembers.Contains(dimension.Member)
                )
                .Select(dimension => new
                {
                    dimension.FinancialFactId,
                    dimension.Axis,
                    dimension.Member,
                })
                .ToListAsync(cancellationToken);
            foreach (var dimension in dimensions)
                classMembers[dimension.FinancialFactId] = (dimension.Axis, dimension.Member);
        }

        var counts = new Dictionary<Guid, SharesOutstandingAsOf>();
        foreach (var issuer in facts.GroupBy(fact => fact.IssuerId))
            if (Pick(issuer.ToList(), classMembers) is { } count)
                counts[issuer.Key] = count;
        return counts;
    }

    /// <summary>
    /// The latest-dated count: the consolidated fact of the latest filing for that date, or, when
    /// per-class counts are stated for a date at least as late, their sum from one filing and one
    /// axis, each class once.
    /// </summary>
    internal static SharesOutstandingAsOf Pick(
        IReadOnlyList<FactRow> facts,
        IReadOnlyDictionary<Guid, (string Axis, string Member)> classMembers
    )
    {
        var consolidated = facts
            .Where(fact => fact.Consolidated && fact.Value > 0)
            .OrderByDescending(fact => fact.PeriodEnd)
            .ThenByDescending(fact => fact.FiledDate)
            .ThenByDescending(fact => fact.AccessionNumber, StringComparer.Ordinal)
            .FirstOrDefault();
        var classFacts = facts.Where(fact => classMembers.ContainsKey(fact.Id)).ToList();
        var latestClass = classFacts
            .OrderByDescending(fact => fact.PeriodEnd)
            .ThenByDescending(fact => fact.FiledDate)
            .ThenByDescending(fact => fact.AccessionNumber, StringComparer.Ordinal)
            .FirstOrDefault();
        if (
            latestClass != null
            && (consolidated == null || latestClass.PeriodEnd >= consolidated.PeriodEnd)
        )
        {
            var axis = classMembers[latestClass.Id].Axis;
            var total = classFacts
                .Where(fact =>
                    fact.AccessionNumber == latestClass.AccessionNumber
                    && fact.PeriodEnd == latestClass.PeriodEnd
                    && classMembers[fact.Id].Axis == axis
                )
                .GroupBy(fact => classMembers[fact.Id].Member, StringComparer.Ordinal)
                .Sum(group => group.First().Value);
            if (total > 0 && total <= long.MaxValue)
                return new SharesOutstandingAsOf(
                    (long)total,
                    latestClass.PeriodEnd,
                    true,
                    ForeignAnnualForms.Contains(latestClass.Form)
                );
        }
        if (consolidated == null || consolidated.Value > long.MaxValue)
            return null;
        return new SharesOutstandingAsOf(
            (long)consolidated.Value,
            consolidated.PeriodEnd,
            false,
            ForeignAnnualForms.Contains(consolidated.Form)
        );
    }

    internal sealed class FactRow
    {
        public Guid Id { get; init; }
        public Guid IssuerId { get; init; }
        public decimal Value { get; init; }
        public DateOnly PeriodEnd { get; init; }
        public DateOnly FiledDate { get; init; }
        public DocumentType Form { get; init; }
        public string AccessionNumber { get; init; }
        public bool Consolidated { get; init; }
    }
}

/// <summary>
/// An issuer's cover-page common share count as of a date, on the basis of <see cref="AsOf"/>.
/// <see cref="IsClassSum"/> marks a sum of per-class counts (the entity total across classes);
/// <see cref="IsForeignForm"/> marks a count from a 20-F or 40-F, stated in ordinary shares.
/// </summary>
public sealed record SharesOutstandingAsOf(
    long Shares,
    DateOnly AsOf,
    bool IsClassSum,
    bool IsForeignForm
);
