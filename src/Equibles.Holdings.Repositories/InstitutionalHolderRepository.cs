using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Repositories;

public class InstitutionalHolderRepository : BaseRepository<InstitutionalHolder>
{
    public InstitutionalHolderRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public async Task<InstitutionalHolder> GetByCik(string cik)
    {
        return await GetAll().FirstOrDefaultAsync(h => h.Cik == cik);
    }

    public IQueryable<InstitutionalHolder> GetByCiks(IEnumerable<string> ciks)
    {
        return GetAll().Where(h => ciks.Contains(h.Cik));
    }

    public IQueryable<InstitutionalHolder> Search(string search)
    {
        // "Name contains the term"; escape so '%' / '_' / '\' match literally, matching SearchNameOrCik.
        var pattern = LikePattern.Contains(search);
        return GetAll().Where(h => EF.Functions.ILike(h.Name, pattern, LikePattern.EscapeChar));
    }

    // Typeahead variant: matches a CIK prefix as well as a name substring so the
    // picker can resolve either "berk" or "1067983" to the same row. The user's
    // input is escaped first so '%' / '_' / '\\' in the query don't behave as LIKE
    // wildcards (e.g. "50%" would otherwise match every name).
    public IQueryable<InstitutionalHolder> SearchNameOrCik(string search)
    {
        var escaped = EscapeLikePattern(search);
        var namePattern = $"%{escaped}%";
        var cikPrefix = $"{EscapeLikePattern(NormalizeCikQuery(search))}%";
        return GetAll()
            .Where(h =>
                EF.Functions.ILike(h.Name, namePattern, LikePattern.EscapeChar)
                || EF.Functions.ILike(h.Cik, cikPrefix, LikePattern.EscapeChar)
            );
    }

    // CIKs are stored unpadded but SEC-canonical form zero-pads them to 10 digits (EDGAR and
    // most sources hand out '0001067983', not '1067983'), so an all-digit query strips its
    // leading zeros before becoming the CIK prefix. An all-zero query keeps the original
    // input — trimming it to empty would turn the prefix into a match-everything '%'.
    private static string NormalizeCikQuery(string search)
    {
        var trimmed = search?.Trim();
        if (string.IsNullOrEmpty(trimmed) || !trimmed.All(char.IsAsciiDigit))
            return search;
        var unpadded = trimmed.TrimStart('0');
        return unpadded.Length == 0 ? search : unpadded;
    }

    // Resolves a name/CIK query to filers ranked largest-first by the biggest 13F filing on
    // record (the InstitutionalFiling rollup's TotalValue). A bare famous name must resolve
    // to the flagship filer: shortest-name and alphabetical orderings both sent "Bridgewater"
    // to Bridgewater Advisors Inc. (a small RIA) instead of Bridgewater Associates, LP.
    // Filers with no 13F rollup rows (13D/G-only filers) rank last; ties break on name length
    // then name so an exact name still beats longer decorated variants at equal size.
    public async Task<List<InstitutionalHolder>> SearchNameOrCikLargestFirst(
        string search,
        int maxResults,
        CancellationToken cancellationToken = default
    )
    {
        var matches = await SearchNameOrCik(search)
            .Select(h => new { h.Id, h.Name })
            .ToListAsync(cancellationToken);
        if (matches.Count == 0)
            return [];

        var ids = matches.Select(m => m.Id).ToList();
        var sizeByHolder = await DbContext
            .Set<InstitutionalFiling>()
            .Where(f => ids.Contains(f.InstitutionalHolderId))
            .GroupBy(f => f.InstitutionalHolderId)
            .Select(g => new { Id = g.Key, MaxTotalValue = g.Max(f => f.TotalValue) })
            .ToDictionaryAsync(x => x.Id, x => x.MaxTotalValue, cancellationToken);

        var topIds = matches
            .OrderByDescending(m => sizeByHolder.TryGetValue(m.Id, out var size) ? size : -1L)
            .ThenBy(m => m.Name.Length)
            .ThenBy(m => m.Name, StringComparer.Ordinal)
            .Take(maxResults)
            .Select(m => m.Id)
            .ToList();

        var holders = await GetAll()
            .Where(h => topIds.Contains(h.Id))
            .ToListAsync(cancellationToken);
        return topIds.Select(id => holders.First(h => h.Id == id)).ToList();
    }

    // Local alias over the shared escaper, kept so SearchNameOrCik reads as one concept.
    private static string EscapeLikePattern(string input) => LikePattern.Escape(input);

    // Distinct non-empty state/country codes across the filer universe, used to
    // populate the location filter dropdown on the institutions index so the user
    // picks from values that actually exist rather than typing a free-form code.
    public IQueryable<string> DistinctStatesOrCountries()
    {
        return GetAll()
            .Where(h => h.StateOrCountry != null && h.StateOrCountry != "")
            .Select(h => h.StateOrCountry)
            .Distinct();
    }

    public IQueryable<InstitutionalHolder> GetUnclassified()
    {
        return GetAll()
            .Where(h => h.Classification == FundClassification.Unknown && h.Name != null);
    }
}
