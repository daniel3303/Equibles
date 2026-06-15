using Equibles.CommonStocks.Data.Models;

namespace Equibles.CommonStocks.HostedService.Services;

/// <summary>
/// Selects and orders the stocks an IR content scraper (news/events) should work
/// through. Stocks classified on a given <see cref="IrPlatformType"/> with a
/// discovered IR URL are returned least-recently-scraped first — never-scraped
/// stocks lead, then the oldest <see cref="CommonStock.IrContentScrapedAt"/>. Each
/// scraper takes a bounded batch off the front and stamps every stock it scrapes, so
/// successive cycles advance through the whole cohort and then refresh it oldest
/// -first, rather than re-scraping the same alphabetical head every cycle.
/// </summary>
public static class IrContentScrapeCandidates
{
    public static IQueryable<CommonStock> ForPlatform(
        IQueryable<CommonStock> stocks,
        IrPlatformType platform
    )
    {
        return stocks
            .Where(s => s.IrPlatformType == platform && s.InvestorRelationsUrl != null)
            // Postgres sorts NULLs last by default; ordering on the null-check first
            // forces never-scraped stocks (false) ahead of already-scraped ones (true).
            .OrderBy(s => s.IrContentScrapedAt != null)
            .ThenBy(s => s.IrContentScrapedAt)
            .ThenBy(s => s.Ticker);
    }
}
