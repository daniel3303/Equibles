using System.Data;
using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Models;
using Equibles.Core.AutoWiring;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using FlexLabs.EntityFrameworkCore.Upsert;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Congress.HostedService.Services;

/// <summary>
/// Mirrors the congress-legislators roster into <see cref="CongressLegislator"/> and
/// <see cref="CongressMemberTerm"/>. Each run replaces the stored terms of every legislator the
/// roster lists, keeping the row id of a term whose key (legislator, chamber, start) is unchanged.
/// </summary>
[Service]
public class CongressLegislatorSyncService
{
    // The current roster lists the whole sitting Congress (about 540); far fewer means a broken
    // or truncated download, which must not delete stored terms.
    internal const int MinimumCurrentLegislators = 500;

    internal const string RejectedLegislatorsErrorContext = "CongressLegislators.Rejected";

    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly CongressLegislatorClient _client;
    private readonly ErrorReporter _errorReporter;
    private readonly ILogger<CongressLegislatorSyncService> _logger;

    public CongressLegislatorSyncService(
        EquiblesFinancialDbContext dbContext,
        CongressLegislatorClient client,
        ErrorReporter errorReporter,
        ILogger<CongressLegislatorSyncService> logger
    )
    {
        _dbContext = dbContext;
        _client = client;
        _errorReporter = errorReporter;
        _logger = logger;
    }

    public async Task SyncAll(CancellationToken ct)
    {
        var current = await _client.GetCurrent(ct);
        var historical = await _client.GetHistorical(ct);
        if (current.RecordCount < MinimumCurrentLegislators || historical.RecordCount == 0)
        {
            throw new InvalidOperationException(
                $"Legislator roster looks incomplete ({current.RecordCount} current, "
                    + $"{historical.RecordCount} historical records); nothing was written"
            );
        }

        var legislators = Merge(current, historical, out var rejections);
        await ReportRejections(rejections);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            ct
        );
        await UpsertLegislators(legislators, ct);
        var counts = await ReplaceTerms(legislators, ct);
        await transaction.CommitAsync(ct);

        _logger.LogInformation(
            "Legislator roster synced: {Legislators} legislators, {Added} terms added, "
                + "{Kept} kept, {Removed} removed, {Rejected} legislators rejected",
            legislators.Count,
            counts.Added,
            counts.Kept,
            counts.Removed,
            rejections.Count
        );
    }

    // The current file wins a BioGuide id both files carry, including when it rejects that record.
    private static List<CongressLegislator> Merge(
        LegislatorRosterParseResult current,
        LegislatorRosterParseResult historical,
        out List<LegislatorRejection> rejections
    )
    {
        var claimed = new HashSet<string>(StringComparer.Ordinal);
        var legislators = new List<CongressLegislator>();
        rejections = [];
        foreach (var file in new[] { current, historical })
        {
            rejections.AddRange(
                file.Rejections.Where(rejection =>
                    rejection.BioguideId == null || claimed.Add(rejection.BioguideId)
                )
            );

            legislators.AddRange(file.Legislators.Where(l => claimed.Add(l.BioguideId)));
        }

        return legislators;
    }

    private async Task ReportRejections(IReadOnlyCollection<LegislatorRejection> rejections)
    {
        if (rejections.Count == 0)
            return;

        foreach (var rejection in rejections)
        {
            _logger.LogWarning(
                "Legislator {BioguideId} rejected: {Reason}",
                rejection.BioguideId,
                rejection.Reason
            );
        }

        await _errorReporter.Report(
            ErrorSource.CongressScraper,
            RejectedLegislatorsErrorContext,
            $"{rejections.Count} roster legislators were left out: "
                + string.Join("; ", rejections.Select(r => $"{r.BioguideId}: {r.Reason}")),
            stackTrace: null
        );
    }

    private async Task UpsertLegislators(
        IReadOnlyCollection<CongressLegislator> legislators,
        CancellationToken ct
    )
    {
        var syncedAt = DateTime.UtcNow;
        var rows = legislators
            .Select(legislator => new CongressLegislator
            {
                BioguideId = legislator.BioguideId,
                FirstName = legislator.FirstName,
                LastName = legislator.LastName,
                Suffix = legislator.Suffix,
                OfficialFullName = legislator.OfficialFullName,
                UpdatedAt = syncedAt,
            })
            .ToList();
        await _dbContext
            .Set<CongressLegislator>()
            .UpsertRange(rows)
            .On(legislator => legislator.BioguideId)
            .WhenMatched(
                (existing, incoming) =>
                    new CongressLegislator
                    {
                        FirstName = incoming.FirstName,
                        LastName = incoming.LastName,
                        Suffix = incoming.Suffix,
                        OfficialFullName = incoming.OfficialFullName,
                        UpdatedAt = incoming.UpdatedAt,
                    }
            )
            .RunAsync(ct);
    }

    private async Task<(int Added, int Kept, int Removed)> ReplaceTerms(
        IReadOnlyCollection<CongressLegislator> legislators,
        CancellationToken ct
    )
    {
        var ids = legislators.Select(legislator => legislator.BioguideId).ToList();
        var stored = await _dbContext
            .Set<CongressMemberTerm>()
            .Where(term => ids.Contains(term.BioguideId))
            .ToDictionaryAsync(Key, ct);

        var added = 0;
        var kept = 0;
        var incomingKeys = new HashSet<(string, CongressPosition, DateOnly)>();
        foreach (var term in legislators.SelectMany(legislator => legislator.Terms))
        {
            var key = Key(term);
            incomingKeys.Add(key);
            if (stored.TryGetValue(key, out var existing))
            {
                existing.State = term.State;
                existing.District = term.District;
                existing.Party = term.Party;
                existing.Caucus = term.Caucus;
                existing.EndDate = term.EndDate;
                kept++;
                continue;
            }

            _dbContext.Add(
                new CongressMemberTerm
                {
                    BioguideId = term.BioguideId,
                    Chamber = term.Chamber,
                    State = term.State,
                    District = term.District,
                    Party = term.Party,
                    Caucus = term.Caucus,
                    StartDate = term.StartDate,
                    EndDate = term.EndDate,
                }
            );
            added++;
        }

        var stale = stored.Where(pair => !incomingKeys.Contains(pair.Key)).ToList();
        _dbContext.RemoveRange(stale.Select(pair => pair.Value));

        await _dbContext.SaveChangesAsync(ct);
        return (added, kept, stale.Count);
    }

    private static (string, CongressPosition, DateOnly) Key(CongressMemberTerm term) =>
        (term.BioguideId, term.Chamber, term.StartDate);
}
