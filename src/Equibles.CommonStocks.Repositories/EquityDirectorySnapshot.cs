using Equibles.CommonStocks.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Equibles.CommonStocks.Repositories;

// Restores the tracked graph after a failed directory save; retained listings are never deleted.
public sealed class EquityDirectorySnapshot
{
    private readonly DbContext _context;
    private readonly EquityIssuer _issuer;
    private readonly List<EquitySecurity> _securities;
    private readonly Dictionary<EquitySecurity, List<EquityListing>> _listings;
    private readonly EquityIssuerPresentation _presentation;
    private readonly EquityListing _primary;
    private readonly List<(
        EntityEntry Entry,
        PropertyValues Current,
        PropertyValues Original,
        EntityState State
    )> _values;

    public EquityDirectorySnapshot(DbContext context, EquityIssuer issuer)
    {
        _context = context;
        _issuer = issuer;
        _securities = issuer.Securities.ToList();
        _listings = _securities.ToDictionary(
            security => security,
            security => security.Listings.ToList()
        );
        _presentation = issuer.Presentation;
        _primary = _presentation?.Listing;
        var entities = new List<object> { issuer };
        entities.AddRange(_securities);
        entities.AddRange(_listings.Values.SelectMany(listings => listings));
        if (_presentation != null)
            entities.Add(_presentation);
        _values = entities
            .Select(entity => context.Entry(entity))
            .Select(entry =>
                (entry, entry.CurrentValues.Clone(), entry.OriginalValues.Clone(), entry.State)
            )
            .ToList();
    }

    public void Restore()
    {
        // Move the retained dependent back before detaching the newly selected principal.
        if (_presentation != null)
        {
            _context
                .Entry(_presentation)
                .Reference(presentation => presentation.Listing)
                .CurrentValue = _primary;
            _presentation.EquityListingId = _primary.Id;
        }
        var retained = _values
            .Select(value => value.Entry.Entity)
            .ToHashSet(ReferenceEqualityComparer.Instance);
        var entries = _context
            .ChangeTracker.Entries()
            .OrderBy(entry =>
                entry.Entity switch
                {
                    EquityIssuerPresentation => 0,
                    EquityListing => 1,
                    EquitySecurity => 2,
                    _ => 3,
                }
            )
            .ToList();
        foreach (var entry in entries)
        {
            var belongs = entry.Entity switch
            {
                EquitySecurity security => security.EquityIssuerId == _issuer.Id,
                EquityListing listing => listing.Security?.EquityIssuerId == _issuer.Id,
                EquityIssuerPresentation presentation => presentation.EquityIssuerId == _issuer.Id,
                _ => false,
            };
            if (belongs && !retained.Contains(entry.Entity))
                entry.State = EntityState.Detached;
        }
        _issuer.Securities = _securities;
        foreach (var (security, listings) in _listings)
            security.Listings = listings;
        _issuer.Presentation = _presentation;
        if (_presentation != null)
            _presentation.Listing = _primary;
        foreach (var value in _values)
        {
            value.Entry.CurrentValues.SetValues(value.Current);
            value.Entry.OriginalValues.SetValues(value.Original);
            value.Entry.State = value.State;
        }
    }
}
