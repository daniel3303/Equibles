namespace Equibles.CommonStocks.BusinessLogic.Websites;

/// <summary>
/// A source of company-website candidates for stocks whose
/// <c>CommonStock.Website</c> is still missing. Implementations live in the module
/// that owns the underlying data (SEC filings, Wikidata, Yahoo, …) and are
/// resolved together by the website discovery worker, which consults them in
/// <see cref="Priority"/> order and only hands each source the stocks every
/// earlier source left unfilled. Candidates are reachability-probed by the
/// caller before anything persists, so sources return what their data says and
/// need not validate the URL themselves.
/// </summary>
public interface IWebsiteSource
{
    /// <summary>
    /// Consultation order: lower values are asked first. Convention: more
    /// authoritative sources get lower values.
    /// </summary>
    int Priority { get; }

    /// <summary>Short human-readable name for logs (e.g. "SEC filings").</summary>
    string Name { get; }

    /// <summary>
    /// Returns a candidate website URL per stock id for every stock this source
    /// has an answer for; stocks it knows nothing about are simply absent from
    /// the result. Implementations are batch-oriented so bulk-friendly backends
    /// (one query for many stocks) stay cheap.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> FindWebsites(
        IReadOnlyList<WebsiteSourceStock> stocks,
        CancellationToken cancellationToken
    );
}
