using Equibles.Data;
using Equibles.Holdings.HostedService.Models;
using Equibles.Sec.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>Reads the registered fund series each adviser manages from its N-CEN reports.</summary>
internal static class AdvisedSeriesLoader
{
    // N-CEN is yearly and reaches SEC's quarterly data sets up to about six months after the
    // period ends, so a current series' latest report can be 18 months old.
    internal const int LookbackMonths = 21;

    /// <summary>
    /// Every primary adviser of every series on a current N-CEN report whose period ends within the
    /// lookback before <paramref name="asOf"/>, and not after it.
    /// </summary>
    internal static Task<List<AdvisedSeriesRow>> Load(
        EquiblesFinancialDbContext dbContext,
        DateOnly asOf,
        CancellationToken cancellationToken
    )
    {
        var since = asOf.AddMonths(-LookbackMonths);
        return dbContext
            .Set<NCenFundAdviser>()
            .AsNoTracking()
            .Where(adviser =>
                adviser.Role == NCenAdviserRole.InvestmentAdviser
                && !adviser.FundReport.IsSuperseded
                && adviser.FundReport.ReportEndingPeriod >= since
                && adviser.FundReport.ReportEndingPeriod <= asOf
            )
            .Select(adviser => new AdvisedSeriesRow(
                adviser.Crd,
                // FundId is per filing; a registrant without series is one fund across its reports.
                adviser.FundReport.SeriesId
                    ?? adviser.FundReport.RegistrantCik,
                adviser.FundReport.ReportEndingPeriod,
                adviser.FundReport.FundTypes,
                // Money market funds report daily average net assets instead (Item C.19.b).
                adviser.FundReport.MonthlyAverageNetAssets
                    ?? adviser.FundReport.DailyAverageNetAssets
            ))
            .ToListAsync(cancellationToken);
    }
}
