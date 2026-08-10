using Equibles.Cftc.Data.Models;
using Equibles.Data;

namespace Equibles.Cftc.Repositories;

public class CftcContractRepository : BaseRepository<CftcContract>
{
    public CftcContractRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<CftcContract> GetByMarketCode(string marketCode)
    {
        var canonical = marketCode?.Trim().ToUpperInvariant();
        var alias = CftcContractSearchAliases.Resolve(marketCode);
        return GetAll()
            .Where(c => c.MarketCode == canonical || (alias != null && c.MarketCode == alias))
            .OrderBy(c => c.MarketCode == canonical ? 0 : 1);
    }

    public IQueryable<CftcContract> GetByCategory(CftcContractCategory category)
    {
        return GetAll().Where(c => c.Category == category);
    }

    public IQueryable<CftcContract> Search(string query)
    {
        var tokens = SearchTerms.Tokenize(query);
        if (tokens.Count == 0)
            return string.IsNullOrWhiteSpace(query) ? GetAll() : GetAll().Where(_ => false);

        var matches = GetAll();
        var anyTokenMatches = GetAll().Where(_ => false);
        foreach (var token in tokens)
        {
            matches = matches.Where(c =>
                c.MarketCode.ToLower().Contains(token) || c.MarketName.ToLower().Contains(token)
            );
            anyTokenMatches = anyTokenMatches.Concat(
                GetAll()
                    .Where(c =>
                        c.MarketCode.ToLower().Contains(token)
                        || c.MarketName.ToLower().Contains(token)
                    )
            );
        }

        var canonical = query.Trim().ToUpperInvariant();
        var exactIdentifier = GetAll().Where(c => c.MarketCode == canonical);
        var aliasCode = CftcContractSearchAliases.Resolve(query);
        var verifiedAlias =
            aliasCode == null
                ? GetAll().Where(_ => false)
                : GetAll().Where(c => c.MarketCode == aliasCode);
        return SearchTerms.WithExclusiveResolutionTiers(
            exactIdentifier,
            verifiedAlias,
            matches,
            anyTokenMatches
        );
    }
}

internal static class CftcContractSearchAliases
{
    private static readonly IReadOnlyDictionary<string, string> MarketCodes = Build();

    public static string Resolve(string query) =>
        MarketCodes.GetValueOrDefault(SearchTerms.Normalize(query));

    private static IReadOnlyDictionary<string, string> Build()
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        Add(aliases, "13874A", "es", "sp500", "s p500", "s p 500", "s p futures", "e mini s p");
        Add(aliases, "209742", "nq", "nasdaq 100", "nasdaq futures");
        Add(aliases, "124603", "ym", "dow futures", "e mini dow");
        Add(aliases, "239742", "rty", "russell 2000", "russell futures");
        Add(aliases, "067651", "cl", "wti", "wti crude", "light sweet crude");
        Add(aliases, "023651", "ng", "natural gas futures");
        Add(aliases, "088691", "gc", "gold futures");
        Add(aliases, "084691", "si", "silver futures");
        Add(aliases, "085692", "hg", "copper futures");
        Add(aliases, "020601", "zb", "treasury bond", "30 year treasury");
        Add(aliases, "043602", "zn", "10 year treasury");
        Add(aliases, "044601", "zf", "5 year treasury");
        Add(aliases, "042601", "zt", "2 year treasury");
        Add(aliases, "1170E1", "vx", "vix", "vix futures");
        Add(aliases, "002602", "zc", "corn futures");
        Add(aliases, "005602", "zs", "soybean futures", "soybeans futures");
        Add(aliases, "001602", "zw", "wheat futures");
        Add(aliases, "099741", "6e", "euro futures");
        Add(aliases, "097741", "6j", "yen futures");
        Add(aliases, "096742", "6b", "pound futures");
        Add(aliases, "232741", "6a", "australian dollar futures");
        Add(aliases, "090741", "6c", "canadian dollar futures");
        Add(aliases, "092741", "6s", "swiss franc futures");
        Add(aliases, "095741", "6m", "mexican peso futures");

        return aliases;
    }

    private static void Add(
        Dictionary<string, string> aliases,
        string marketCode,
        params string[] names
    )
    {
        foreach (var name in names)
            aliases[SearchTerms.Normalize(name)] = marketCode;
    }
}
