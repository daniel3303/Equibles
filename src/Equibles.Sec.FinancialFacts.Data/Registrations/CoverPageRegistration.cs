using Equibles.CommonStocks.Data.Helpers;
using Equibles.Sec.FinancialFacts.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.FinancialFacts.Data.Registrations;

/// <summary>
/// Two symbols on one 12(b) cover page are distinct classes registered together, never a renamed
/// predecessor and its successor, so identity corrections use this as their proof.
/// </summary>
public static class CoverPageRegistration
{
    public static bool RegisteredTogether(
        IEnumerable<(string Symbol, string Accession)> registrations,
        string first,
        string second
    )
    {
        var firstIdentity = TickerNormalizer.NormalizeIdentity(first);
        var secondIdentity = TickerNormalizer.NormalizeIdentity(second);
        if (firstIdentity == null || secondIdentity == null || firstIdentity == secondIdentity)
            return false;
        var accessionsBySymbol = registrations
            .Where(registration => !string.IsNullOrWhiteSpace(registration.Accession))
            .ToLookup(
                registration => TickerNormalizer.NormalizeIdentity(registration.Symbol),
                registration => registration.Accession
            );
        return accessionsBySymbol[firstIdentity]
            .Intersect(accessionsBySymbol[secondIdentity])
            .Any();
    }

    /// <summary>Each issuer's filed symbols with the accession that stated them; every issuer when null.</summary>
    public static async Task<Dictionary<Guid, List<(string Symbol, string Accession)>>> Load(
        DbContext dbContext,
        IReadOnlyCollection<Guid> issuerIds,
        CancellationToken cancellationToken
    )
    {
        var rows = dbContext.Set<IssuerSecurityRegistration>().AsNoTracking();
        if (issuerIds != null)
            rows = rows.Where(row => issuerIds.Contains(row.EquityIssuerId));
        var loaded = await rows.Select(row => new
            {
                row.EquityIssuerId,
                row.TradingSymbol,
                row.AccessionNumber,
            })
            .ToListAsync(cancellationToken);
        return loaded
            .GroupBy(row => row.EquityIssuerId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => (row.TradingSymbol, row.AccessionNumber)).ToList()
            );
    }
}
