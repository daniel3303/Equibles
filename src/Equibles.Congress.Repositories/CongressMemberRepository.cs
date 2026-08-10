using Equibles.Congress.Data.Models;
using Equibles.Data;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Congress.Repositories;

public class CongressMemberRepository : BaseRepository<CongressMember>
{
    public CongressMemberRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public async Task<CongressMember> GetByName(string name)
    {
        var exactName = name?.Trim().ToLowerInvariant();
        var exact = await GetAll().FirstOrDefaultAsync(m => m.Name.ToLower() == exactName);
        if (exact != null)
            return exact;

        var canonicalName = CongressMemberSearchAliases.ResolveName(name);
        return canonicalName == null
            ? null
            : await GetAll()
                .FirstOrDefaultAsync(m => m.Name.ToLower() == canonicalName.ToLowerInvariant());
    }

    public IQueryable<CongressMember> Search(string search)
    {
        var tokens = SearchTerms.Tokenize(search);
        if (tokens.Count == 0 && !string.IsNullOrWhiteSpace(search))
            return GetAll().Where(_ => false);

        var matches = GetAll();
        var anyTokenMatches = GetAll().Where(_ => false);
        foreach (var token in tokens)
        {
            var pattern = LikePattern.Contains(token);
            matches = matches.Where(m =>
                EF.Functions.ILike(m.Name, pattern, LikePattern.EscapeChar)
            );
            anyTokenMatches = anyTokenMatches.Concat(
                GetAll().Where(m => EF.Functions.ILike(m.Name, pattern, LikePattern.EscapeChar))
            );
        }

        var exactName = search?.Trim().ToLowerInvariant();
        var exactIdentifier =
            exactName == null
                ? GetAll().Where(_ => false)
                : GetAll().Where(m => m.Name.ToLower() == exactName);
        var canonicalName = CongressMemberSearchAliases.ResolveName(search);
        var verifiedAlias =
            canonicalName == null
                ? GetAll().Where(_ => false)
                : GetAll().Where(m => m.Name.ToLower() == canonicalName.ToLowerInvariant());
        return SearchTerms.WithExclusiveResolutionTiers(
            exactIdentifier,
            verifiedAlias,
            matches,
            anyTokenMatches
        );
    }
}

internal static class CongressMemberSearchAliases
{
    private static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<
        string,
        string
    >(StringComparer.OrdinalIgnoreCase)
    {
        // The House roster files the member as Daniel Crenshaw, while the public and
        // the tool examples use Dan Crenshaw.
        ["dan crenshaw"] = "Daniel Crenshaw",
    };

    public static string ResolveName(string query) =>
        Names.GetValueOrDefault(SearchTerms.Normalize(query));
}
