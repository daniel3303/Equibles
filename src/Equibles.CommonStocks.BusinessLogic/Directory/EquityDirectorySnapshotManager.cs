using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.CommonStocks.BusinessLogic.Directory;

[Service]
public class EquityDirectorySnapshotManager(IServiceScopeFactory scopeFactory)
{
    public async Task<Guid> Reconcile(
        EquityDirectorySnapshotInput input,
        CancellationToken token = default
    )
    {
        if (
            string.IsNullOrWhiteSpace(input.Source)
            || string.IsNullOrWhiteSpace(input.EvidenceSource)
            || string.IsNullOrWhiteSpace(input.SourceRecordKey)
            || string.IsNullOrWhiteSpace(input.PayloadJson)
            || input.ObservedAt == default
            || input.ObservedAt.Kind != DateTimeKind.Utc
            || string.IsNullOrWhiteSpace(input.MarketCountryCode)
            || input.MarketIdentifierCodes.Count == 0
            || input.Listings.Count == 0
            || input.Listings.Any(row =>
                string.IsNullOrWhiteSpace(row.Isin)
                || string.IsNullOrWhiteSpace(row.Ticker)
                || !input.MarketIdentifierCodes.Contains(row.MarketIdentifierCode)
            )
            || input.Listings.DistinctBy(row => (row.Isin, row.MarketIdentifierCode)).Count()
                != input.Listings.Count
            || input.Listings.DistinctBy(row => (row.MarketIdentifierCode, row.Ticker)).Count()
                != input.Listings.Count
        )
            throw new InvalidDataException(
                "A complete, unambiguous directory snapshot is required."
            );
        await using var scope = scopeFactory.CreateAsyncScope();
        var issuers = scope.ServiceProvider.GetRequiredService<EquityIssuerRepository>();
        var listings = scope.ServiceProvider.GetRequiredService<EquityListingRepository>();
        var evidence =
            scope.ServiceProvider.GetRequiredService<EquityDirectorySourceRecordRepository>();
        await using var transaction = await issuers.BeginDirectoryIdentityWrite(token);
        if (transaction == null)
            throw new InvalidOperationException(
                "Directory reconciliation requires an independent relational transaction."
            );
        var state = await evidence
            .GetSnapshotStates()
            .SingleOrDefaultAsync(
                row => row.Source == input.Source && row.SourceRecordKey == input.SourceRecordKey,
                token
            );
        if (state != null && state.ObservedAt > input.ObservedAt)
            throw new InvalidDataException("A newer directory snapshot is already current.");
        var recordId = await evidence.Append(
            input.EvidenceSource,
            input.SourceRecordKey,
            input.PayloadJson,
            token
        );
        if (
            state != null
            && state.ObservedAt == input.ObservedAt
            && state.SourceRecordId != recordId
        )
            throw new InvalidDataException("Equal-time directory snapshots disagree.");
        var current = input.Listings.ToHashSet();
        var existing = await listings
            .GetAll()
            .Where(row =>
                row.Active
                && row.IsDirectoryListed
                && row.MarketCountryCode == input.MarketCountryCode
                && input.MarketIdentifierCodes.Contains(row.MarketIdentifierCode)
            )
            .Include(row => row.Security)
            .ToListAsync(token);
        foreach (var listing in existing)
        {
            if (
                current.Contains(
                    new(listing.Security.Isin, listing.MarketIdentifierCode, listing.Ticker)
                )
            )
                continue;
            // Absence withdraws current capture eligibility; it supplies no effective delisting date.
            listing.Active = false;
            listing.IsDirectoryListed = false;
        }
        if (state == null)
        {
            state = new EquityDirectorySnapshotState
            {
                Source = input.Source,
                SourceRecordKey = input.SourceRecordKey,
            };
            evidence.AddSnapshotState(state);
        }
        state.SourceRecordId = recordId;
        state.ObservedAt = input.ObservedAt;
        await issuers.SaveChanges();
        await transaction.CommitAsync(token);
        return recordId;
    }
}
