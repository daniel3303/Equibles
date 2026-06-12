using Equibles.CommonStocks.BusinessLogic.Websites;
using Equibles.Integrations.Wikidata.Contracts;

namespace Equibles.CommonStocks.HostedService.Services;

/// <summary>
/// Website source backed by Wikidata: joins the stocks' SEC CIK to the entity's
/// official website (one bulk SPARQL query per batch). Secondary to the filings
/// source — community-maintained rather than self-reported — but an exact-key
/// match that also covers companies whose stored filings carry no disclosure
/// (e.g. foreign issuers). A stale entry fails the caller's reachability probe
/// and falls through to the next source.
/// </summary>
public class WikidataWebsiteSource : IWebsiteSource
{
    private readonly IWikidataClient _wikidataClient;

    public WikidataWebsiteSource(IWikidataClient wikidataClient)
    {
        _wikidataClient = wikidataClient;
    }

    public int Priority => 20;

    public string Name => "Wikidata";

    public async Task<IReadOnlyDictionary<Guid, string>> FindWebsites(
        IReadOnlyList<WebsiteSourceStock> stocks,
        CancellationToken cancellationToken
    )
    {
        var stocksByCik = stocks
            .Where(s => !string.IsNullOrWhiteSpace(s.Cik))
            .GroupBy(s => s.Cik)
            .ToDictionary(g => g.Key, g => g.First());
        if (stocksByCik.Count == 0)
            return new Dictionary<Guid, string>();

        var websitesByCik = await _wikidataClient.GetOfficialWebsitesByCik(
            stocksByCik.Keys,
            cancellationToken
        );

        return websitesByCik.ToDictionary(pair => stocksByCik[pair.Key].Id, pair => pair.Value);
    }
}
