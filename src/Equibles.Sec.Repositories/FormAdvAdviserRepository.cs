using Equibles.Data;
using Equibles.Sec.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Repositories;

public class FormAdvAdviserRepository : BaseRepository<FormAdvAdviser>
{
    public FormAdvAdviserRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<FormAdvAdviser> GetByCrd(int crd)
    {
        return GetAll().Where(a => a.Crd == crd);
    }

    /// <summary>
    /// Matches advisers by punctuation-independent legal/business-name tokens, all-token first
    /// with an any-token fallback only when strict matching has no rows. Orders by regulatory
    /// assets under management and returns nothing for a blank term.
    /// </summary>
    public IQueryable<FormAdvAdviser> Search(string term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return GetAll().Where(a => false);
        }

        var tokens = SearchTerms.Tokenize(term);
        if (tokens.Count == 0)
            return GetAll().Where(_ => false);

        var matches = GetAll();
        var anyTokenMatches = GetAll().Where(_ => false);
        foreach (var token in tokens)
        {
            var pattern = LikePattern.Contains(token);
            matches = matches.Where(a =>
                EF.Functions.ILike(a.LegalName, pattern, LikePattern.EscapeChar)
                || EF.Functions.ILike(a.PrimaryBusinessName, pattern, LikePattern.EscapeChar)
            );
            anyTokenMatches = anyTokenMatches.Concat(
                GetAll()
                    .Where(a =>
                        EF.Functions.ILike(a.LegalName, pattern, LikePattern.EscapeChar)
                        || EF.Functions.ILike(
                            a.PrimaryBusinessName,
                            pattern,
                            LikePattern.EscapeChar
                        )
                    )
            );
        }

        return SearchTerms
            .WithSparseAnyTokenFallback(matches, anyTokenMatches)
            // Coalesce so advisers that did not report assets sort last rather than first
            // (Postgres orders NULL highest under a plain DESC).
            .OrderByDescending(a => a.TotalRegulatoryAum ?? 0L);
    }

    /// <summary>Advisers ordered by total regulatory assets under management, largest first.</summary>
    public IQueryable<FormAdvAdviser> GetLargestByAum()
    {
        return GetAll().OrderByDescending(a => a.TotalRegulatoryAum ?? 0L);
    }
}
