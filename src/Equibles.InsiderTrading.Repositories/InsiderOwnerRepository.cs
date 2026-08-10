using System.Text.RegularExpressions;
using Equibles.Data;
using Equibles.InsiderTrading.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.InsiderTrading.Repositories;

public class InsiderOwnerRepository : BaseRepository<InsiderOwner>
{
    public InsiderOwnerRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public async Task<InsiderOwner> GetByOwnerCik(string ownerCik)
    {
        return await GetAll().FirstOrDefaultAsync(o => o.OwnerCik == ownerCik);
    }

    public IQueryable<InsiderOwner> GetByOwnerCiks(IEnumerable<string> ownerCiks)
    {
        return GetAll().Where(o => ownerCiks.Contains(o.OwnerCik));
    }

    public IQueryable<InsiderOwner> Search(string search)
    {
        var tokens = SearchTerms.Tokenize(search);
        if (tokens.Count == 0 && !string.IsNullOrWhiteSpace(search))
            return GetAll().Where(_ => false);

        var matches = GetAll();
        var anyTokenMatches = GetAll().Where(_ => false);
        foreach (var token in tokens)
        {
            var pattern = $"(^|[^[:alnum:]]){Regex.Escape(token)}($|[^[:alnum:]])";
            matches = matches.Where(o => Regex.IsMatch(o.Name, pattern, RegexOptions.IgnoreCase));
            anyTokenMatches = anyTokenMatches.Concat(
                GetAll().Where(o => Regex.IsMatch(o.Name, pattern, RegexOptions.IgnoreCase))
            );
        }

        // An exact filed name is authoritative. Otherwise explicit CIK aliases bridge public
        // names to verified filed identities before ordinary token matching.
        var exactName = search?.Trim().ToLowerInvariant();
        var exactIdentifier =
            exactName == null
                ? GetAll().Where(_ => false)
                : GetAll().Where(o => o.Name.ToLower() == exactName);
        var aliasCik = InsiderOwnerSearchAliases.ResolveCik(search);
        var verifiedAlias =
            aliasCik == null
                ? GetAll().Where(_ => false)
                : GetAll().Where(o => o.OwnerCik == aliasCik);
        return SearchTerms.WithExclusiveResolutionTiers(
            exactIdentifier,
            verifiedAlias,
            matches,
            anyTokenMatches
        );
    }
}

internal static class InsiderOwnerSearchAliases
{
    private static readonly IReadOnlyDictionary<string, string> OwnerCiks = new Dictionary<
        string,
        string
    >(StringComparer.OrdinalIgnoreCase)
    {
        ["jensen huang"] = "0001197649",
        ["jen hsun huang"] = "0001197649",
    };

    public static string ResolveCik(string query) =>
        OwnerCiks.GetValueOrDefault(SearchTerms.Normalize(query));
}
