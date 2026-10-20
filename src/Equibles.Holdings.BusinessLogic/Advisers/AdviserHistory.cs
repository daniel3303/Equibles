using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.Sec.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.BusinessLogic.Advisers;

/// <summary>
/// Reads Form ADV facts as they stood at past dates from the roster version history. The roster
/// current at a date is the newest one SEC published on or before it; the date is covered only
/// once the history has folded that roster in, so a quarter never borrows a later roster's
/// client base. A registered listing wins over an exempt reporting one.
/// </summary>
public static class AdviserHistory
{
    private static readonly FormAdvRegistration[] Precedence =
    [
        FormAdvRegistration.Registered,
        FormAdvRegistration.ExemptReporting,
    ];

    /// <summary>
    /// The facts valid at each date for every adviser <paramref name="crds"/> selects; an adviser
    /// missing from a date's roster, or a date the history does not cover yet, has no entry.
    /// </summary>
    public static async Task<Dictionary<(int Crd, DateOnly Date), AdviserFacts>> FactsAt(
        InstitutionalHolderRepository repository,
        IReadOnlyCollection<int> crds,
        IReadOnlyCollection<DateOnly> dates,
        CancellationToken cancellationToken = default
    )
    {
        // Holders without an adviser listing never touch the Form ADV tables.
        if (crds.Count == 0 || dates.Count == 0)
            return [];
        var rosters = await LoadRosters(repository, cancellationToken);
        var current = dates
            .Distinct()
            .SelectMany(date =>
                Precedence.Select(registration =>
                    (
                        Date: date,
                        Registration: registration,
                        Roster: rosters.CurrentAt(registration, date)
                    )
                )
            )
            .Where(item => item.Roster != null)
            .ToList();
        if (current.Count == 0)
            return [];

        var first = current.Min(item => item.Roster.Value);
        var last = current.Max(item => item.Roster.Value);
        var versions = await LoadVersions(
            repository
                .GetAdviserVersions()
                .Where(version =>
                    crds.Contains(version.Crd)
                    && version.ReportDate <= last
                    && version.LastSeenReportDate >= first
                ),
            cancellationToken
        );
        var latestDate = dates.Max();
        var earliestFiling = dates
            .Min()
            .AddMonths(-AdviserPrivateFundFocus.ScheduleDLookbackMonths);
        var filings = (
            await repository
                .GetPrivateFundFilings()
                .AsNoTracking()
                .Where(filing =>
                    crds.Contains(filing.Crd)
                    && filing.FiledOn <= latestDate
                    && filing.FiledOn >= earliestFiling
                )
                .ToListAsync(cancellationToken)
        ).ToLookup(filing => filing.Crd);

        var facts = new Dictionary<(int, DateOnly), AdviserFacts>();
        var versionsByCrd = versions.ToLookup(version => version.Crd);
        foreach (var crd in versionsByCrd.Select(group => group.Key))
        {
            foreach (var date in dates.Distinct())
            {
                foreach (var registration in Precedence)
                {
                    if (rosters.CurrentAt(registration, date) is not { } roster)
                        continue;
                    var version = versionsByCrd[crd]
                        .FirstOrDefault(row =>
                            row.Registration == registration
                            && row.ReportDate <= roster
                            && row.LastSeenReportDate >= roster
                        );
                    if (version == null)
                        continue;
                    facts[(crd, date)] = ToFacts(
                        version,
                        roster,
                        NewestFiling(filings[crd], date),
                        date
                    );
                    break;
                }
            }
        }
        return facts;
    }

    /// <summary>
    /// Every stretch of one adviser's roster history, oldest first, each with its facts as of its
    /// last roster; empty when the history holds none.
    /// </summary>
    public static async Task<List<AdviserFacts>> Timeline(
        InstitutionalHolderRepository repository,
        int crd,
        CancellationToken cancellationToken = default
    )
    {
        var versions = await LoadVersions(
            repository.GetAdviserVersions().Where(version => version.Crd == crd),
            cancellationToken
        );
        var filings = await repository
            .GetPrivateFundFilings()
            .AsNoTracking()
            .Where(filing => filing.Crd == crd)
            .ToListAsync(cancellationToken);
        return versions
            .OrderBy(version => version.ReportDate)
            .ThenBy(version => version.Registration)
            .Select(version =>
                ToFacts(
                    version,
                    version.LastSeenReportDate,
                    NewestFiling(filings, version.LastSeenReportDate),
                    version.LastSeenReportDate
                )
            )
            .ToList();
    }

    /// <summary>The distinct adviser CRDs among holder profiles, skipping holders without one.</summary>
    public static List<int> Crds(IEnumerable<int?> adviserCrds) =>
        adviserCrds.OfType<int>().Distinct().ToList();

    /// <summary>
    /// A timeline merged where regulatory assets, client base and private funds stay the same, so
    /// each entry is one change a reader can see; newest first. Stretches on different rosters
    /// never merge.
    /// </summary>
    public static List<AdviserFacts> Changes(IReadOnlyList<AdviserFacts> timeline)
    {
        var merged = new List<AdviserFacts>();
        foreach (var facts in timeline.OrderBy(facts => facts.Since))
        {
            var previous = merged.LastOrDefault();
            if (previous != null && SameVisibleFacts(previous, facts))
                merged[^1] = facts with { Since = previous.Since };
            else
                merged.Add(facts);
        }
        merged.Reverse();
        return merged;
    }

    private static bool SameVisibleFacts(AdviserFacts a, AdviserFacts b) =>
        a.Registration == b.Registration
        && a.RegulatoryAum == b.RegulatoryAum
        && a.ClientBase == b.ClientBase
        && Round(a.ClientBaseAumShare) == Round(b.ClientBaseAumShare)
        && Round(a.PooledVehicleAumShare) == Round(b.PooledVehicleAumShare)
        && a.AdvisesPrivateFunds == b.AdvisesPrivateFunds
        && a.PrivateFundCount == b.PrivateFundCount
        && a.PrivateFundGrossAssets == b.PrivateFundGrossAssets;

    // Shares are shown to a tenth of a percent.
    private static decimal? Round(decimal? share) =>
        share is { } value ? Math.Round(value, 3) : null;

    /// <summary>
    /// The adviser's newest Schedule D 7.B.(1) filing in SEC's archive, with its filing date as the
    /// as-of date; null when the archive lists none.
    /// </summary>
    public static Task<FormAdvPrivateFundFiling> LatestPrivateFundFiling(
        InstitutionalHolderRepository repository,
        int crd,
        CancellationToken cancellationToken = default
    ) =>
        repository
            .GetPrivateFundFilings()
            .AsNoTracking()
            .Where(filing => filing.Crd == crd)
            .OrderByDescending(filing => filing.FiledOn)
            .ThenByDescending(filing => filing.FilingId)
            .FirstOrDefaultAsync(cancellationToken);

    private static FormAdvPrivateFundFiling NewestFiling(
        IEnumerable<FormAdvPrivateFundFiling> filings,
        DateOnly date
    ) =>
        filings
            .Where(filing => filing.FiledOn <= date)
            .OrderByDescending(filing => filing.FiledOn)
            .ThenByDescending(filing => filing.FilingId)
            .FirstOrDefault();

    private static AdviserFacts ToFacts(
        VersionRow version,
        DateOnly roster,
        FormAdvPrivateFundFiling scheduleD,
        DateOnly asOf
    )
    {
        var (clientBase, share, pooled) = AdviserClientBase.Of(
            version.ClientTypes.Select(row => (row.ClientType, row.RegulatoryAum))
        );
        var (focus, basis, focusAsOf) =
            version.AdvisesPrivateFunds == false
                ? (PrivateFundFocus.None, null, null)
                : AdviserPrivateFundFocus.Decide(version.FundCounts, roster, scheduleD, asOf);
        return new AdviserFacts
        {
            Crd = version.Crd,
            Registration = version.Registration,
            RosterDate = roster,
            Since = version.ReportDate,
            Until = version.LastSeenReportDate,
            LatestFilingDate = version.LatestFilingDate,
            RegulatoryAum = version.TotalRegulatoryAum,
            ClientBase = clientBase,
            ClientBaseAumShare = share,
            PooledVehicleAumShare = pooled,
            AdvisesPrivateFunds = version.AdvisesPrivateFunds,
            PrivateFundCount = version.PrivateFundCount,
            PrivateFundGrossAssets = version.PrivateFundGrossAssets,
            PrivateFundFocus = focus,
            PrivateFundFocusBasis = basis,
            PrivateFundFocusAsOf = focusAsOf,
        };
    }

    private static async Task<List<VersionRow>> LoadVersions(
        IQueryable<FormAdvAdviserVersion> versions,
        CancellationToken cancellationToken
    ) => (
            await versions
                .AsNoTracking()
                .Select(version => new
                {
                    version.Crd,
                    version.Registration,
                    version.ReportDate,
                    version.LastSeenReportDate,
                    version.LatestFilingDate,
                    version.TotalRegulatoryAum,
                    version.AdvisesPrivateFunds,
                    version.PrivateFundCount,
                    version.PrivateFundGrossAssets,
                    version.HedgeFundCount,
                    version.PrivateEquityFundCount,
                    version.VentureCapitalFundCount,
                    version.RealEstateFundCount,
                    version.LiquidityFundCount,
                    version.SecuritizedAssetFundCount,
                    version.OtherPrivateFundCount,
                    ClientTypes = version
                        .ClientTypes.Select(row => new { row.ClientType, row.RegulatoryAum })
                        .ToList(),
                })
                .ToListAsync(cancellationToken)
        ).Select(row => new VersionRow(row.Crd, row.Registration, row.ReportDate, row.LastSeenReportDate, row.LatestFilingDate, row.TotalRegulatoryAum, row.AdvisesPrivateFunds, row.PrivateFundCount, row.PrivateFundGrossAssets, new RosterFundCounts(row.HedgeFundCount, row.PrivateEquityFundCount, row.VentureCapitalFundCount, row.RealEstateFundCount, row.LiquidityFundCount, row.SecuritizedAssetFundCount, row.OtherPrivateFundCount), row.ClientTypes.Select(type => (type.ClientType, type.RegulatoryAum)).ToList())).ToList();

    private static async Task<RosterCalendar> LoadRosters(
        InstitutionalHolderRepository repository,
        CancellationToken cancellationToken
    ) =>
        new(
            await repository
                .GetAdviserRosters()
                .AsNoTracking()
                .Select(roster => new RosterCalendar.Entry(
                    roster.Registration,
                    roster.ReportDate,
                    roster.VersionedAt != null,
                    roster.AdviserCount
                ))
                .ToListAsync(cancellationToken)
        );

    private sealed record VersionRow(
        int Crd,
        FormAdvRegistration Registration,
        DateOnly ReportDate,
        DateOnly LastSeenReportDate,
        DateOnly? LatestFilingDate,
        long? TotalRegulatoryAum,
        bool? AdvisesPrivateFunds,
        int? PrivateFundCount,
        long? PrivateFundGrossAssets,
        RosterFundCounts FundCounts,
        List<(FormAdvClientType ClientType, long? RegulatoryAum)> ClientTypes
    );
}
