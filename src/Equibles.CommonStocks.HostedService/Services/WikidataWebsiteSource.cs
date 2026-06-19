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
        var stockGroups = stocks
            .Where(s => !string.IsNullOrWhiteSpace(s.Cik))
            .GroupBy(s => s.Cik)
            .ToList();
        if (stockGroups.Count == 0)
            return new Dictionary<Guid, string>();

        var websitesByCik = await _wikidataClient.GetOfficialWebsitesByCik(
            stockGroups.Select(g => g.Key).ToList(),
            cancellationToken
        );

        // One Wikidata website per CIK fans out to every stock sharing that CIK
        // (dual-class issuers like GOOGL/GOOG), not just the first one seen.
        return stockGroups
            .Where(g => websitesByCik.ContainsKey(g.Key))
            .SelectMany(g => g.Select(s => (s.Id, Website: websitesByCik[g.Key])))
            .ToDictionary(x => x.Id, x => x.Website);
    }
}
