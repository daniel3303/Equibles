using Equibles.Data;
using Equibles.Sec.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Folds one batch of a roster into the adviser version history. Rosters of a kind arrive strictly
/// oldest first: an adviser whose content hash matches its newest version, and who was listed on
/// the previous roster, only moves that version's last-seen date; any other adviser starts a new
/// version. A batch already folded in (a retried cycle) changes nothing.
/// </summary>
internal static class FormAdvVersionWriter
{
    /// <summary>Writes inside the caller's transaction; returns the versions started.</summary>
    internal static async Task<int> Write(
        EquiblesFinancialDbContext dbContext,
        IReadOnlyList<FormAdvAdviserVersion> incoming,
        FormAdvRegistration registration,
        DateOnly reportDate,
        DateOnly? previousReportDate
    )
    {
        var byCrd = incoming
            .GroupBy(version => version.Crd)
            .ToDictionary(group => group.Key, group => group.First());
        var crds = byCrd.Keys.ToList();
        var versions = await dbContext
            .Set<FormAdvAdviserVersion>()
            .AsNoTracking()
            .Where(version => version.Registration == registration && crds.Contains(version.Crd))
            .Select(version => new
            {
                version.Id,
                version.Crd,
                version.ReportDate,
                version.LastSeenReportDate,
                version.ContentHash,
            })
            .ToListAsync();
        var heads = versions
            .GroupBy(version => version.Crd)
            .ToDictionary(group => group.Key, group => group.MaxBy(version => version.ReportDate));

        var extended = new List<Guid>();
        var started = new List<FormAdvAdviserVersion>();
        foreach (var (crd, version) in byCrd)
        {
            var head = heads.GetValueOrDefault(crd);
            if (head != null && head.LastSeenReportDate >= reportDate)
                continue;
            if (
                head != null
                && head.ContentHash == version.ContentHash
                && previousReportDate is { } previous
                && head.LastSeenReportDate == previous
            )
                extended.Add(head.Id);
            else
                started.Add(version);
        }

        if (extended.Count > 0)
            await dbContext
                .Set<FormAdvAdviserVersion>()
                .Where(version => extended.Contains(version.Id))
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(version => version.LastSeenReportDate, reportDate)
                );
        if (started.Count > 0)
        {
            dbContext.Set<FormAdvAdviserVersion>().AddRange(started);
            await dbContext.SaveChangesAsync();
            dbContext.ChangeTracker.Clear();
        }

        // The current view keeps advisers that left the rosters; mark the ones this roster lists.
        await dbContext
            .Set<FormAdvAdviser>()
            .Where(adviser =>
                crds.Contains(adviser.Crd)
                && (adviser.LastSeenReportDate == null || adviser.LastSeenReportDate < reportDate)
            )
            .ExecuteUpdateAsync(setters =>
                setters.SetProperty(adviser => adviser.LastSeenReportDate, reportDate)
            );
        return started.Count;
    }
}
