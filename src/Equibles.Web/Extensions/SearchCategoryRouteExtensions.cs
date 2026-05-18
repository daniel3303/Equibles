using Equibles.Search.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Equibles.Web.Extensions;

/// <summary>
/// Maps framework-agnostic search hits to MVC routes. Keeping this in the Web layer is what lets
/// providers stay MVC-free (they emit only <see cref="SearchHit.Kind"/> + RouteValues). A hit whose
/// kind has no destination page returns null and the view renders it as plain text.
/// </summary>
public static class SearchCategoryRouteExtensions
{
    public static string HitUrl(this IUrlHelper url, SearchHit hit)
    {
        return hit.Kind switch
        {
            "Stock" => url.Action(
                "Show",
                "Stocks",
                new { ticker = hit.RouteValues.GetValueOrDefault("ticker") }
            ),
            "Filing" => url.Action(
                "ShowDocument",
                "Stocks",
                new
                {
                    ticker = hit.RouteValues.GetValueOrDefault("ticker"),
                    id = hit.RouteValues.GetValueOrDefault("id"),
                }
            ),
            "EconomicSeries" => url.Action(
                "Show",
                "EconomicData",
                new { seriesId = hit.RouteValues.GetValueOrDefault("seriesId") }
            ),
            "FuturesMarket" => url.Action(
                "Show",
                "Cftc",
                new { marketCode = hit.RouteValues.GetValueOrDefault("marketCode") }
            ),
            "Institution" => url.Action(
                "Institution",
                "Profiles",
                new { cik = hit.RouteValues.GetValueOrDefault("cik") }
            ),
            "Insider" => url.Action(
                "Insider",
                "Profiles",
                new { ownerCik = hit.RouteValues.GetValueOrDefault("ownerCik") }
            ),
            "CongressMember" => url.Action(
                "Member",
                "Profiles",
                new { id = hit.RouteValues.GetValueOrDefault("id") }
            ),
            // Every shipped Kind resolves above. A new provider Kind falling here is a gap —
            // SearchCategoryRouteMappingTests pins the allowlist so it can't ship dead entries.
            _ => null,
        };
    }

    /// <summary>"See all" destination for a group, or null when no browse page exists.</summary>
    public static string CategoryUrl(this IUrlHelper url, string category, string query)
    {
        return category switch
        {
            "Stocks" => url.Action("Index", "Stocks", new { search = query }),
            "Economic Indicators" => url.Action("Index", "EconomicData"),
            "Futures" => url.Action("Index", "Cftc"),
            _ => null,
        };
    }
}
