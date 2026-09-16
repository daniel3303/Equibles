using Equibles.CommonStocks.BusinessLogic.Websites;
using Equibles.Integrations.Wikidata.Contracts;

namespace Equibles.CommonStocks.HostedService.Services;

/// <summary>
/// Website source backed by Wikidata: joins an SEC registrant's CIK, or a CIK-less
/// issuer's Legal Entity Identifier, to the entity's official website (one bulk
/// SPARQL query per key kind per batch). Secondary to the filings source,
/// community-maintained rather than self-reported, but an exact-key match that
/// also covers companies whose stored filings carry no disclosure (foreign
/// issuers, verified venue listings). A stale entry fails the caller's
/// reachability probe and falls through to the next source.
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
        var results = new Dictionary<Guid, string>();

        var byCik = stocks
            .Where(s => !string.IsNullOrWhiteSpace(s.Cik))
            .GroupBy(s => s.Cik)
            .ToList();
        if (byCik.Count > 0)
        {
            var websitesByCik = await _wikidataClient.GetOfficialWebsitesByCik(
                byCik.Select(g => g.Key).ToList(),
                cancellationToken
            );
            Merge(results, byCik, websitesByCik);
        }

        // An issuer with no SEC registration (every verified venue listing) joins on its LEI.
        var byLei = stocks
            .Where(s =>
                string.IsNullOrWhiteSpace(s.Cik)
                && !string.IsNullOrWhiteSpace(s.LegalEntityIdentifier)
            )
            .GroupBy(s => s.LegalEntityIdentifier)
            .ToList();
        if (byLei.Count > 0)
        {
            var websitesByLei = await _wikidataClient.GetOfficialWebsitesByLei(
                byLei.Select(g => g.Key).ToList(),
                cancellationToken
            );
            Merge(results, byLei, websitesByLei);
        }

        return results;
    }

    // One Wikidata website per key fans out to every stock sharing that key
    // (dual-class issuers like GOOGL/GOOG), not just the first one seen.
    private static void Merge(
        Dictionary<Guid, string> results,
        IEnumerable<IGrouping<string, WebsiteSourceStock>> groups,
        IReadOnlyDictionary<string, string> websites
    )
    {
        foreach (var group in groups)
        {
            if (!websites.TryGetValue(group.Key, out var website))
                continue;
            foreach (var stock in group)
                results[stock.Id] = website;
        }
    }
}
