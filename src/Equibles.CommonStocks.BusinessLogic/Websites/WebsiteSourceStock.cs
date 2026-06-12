namespace Equibles.CommonStocks.BusinessLogic.Websites;

/// <summary>
/// The identifying slice of a <c>CommonStock</c> handed to
/// <see cref="IWebsiteSource"/> implementations — enough for any backend to key
/// its lookup (documents by stock id, Wikidata by CIK, Yahoo by ticker) without
/// passing tracked entities between service scopes.
/// </summary>
public sealed record WebsiteSourceStock(Guid Id, string Ticker, string Cik);
