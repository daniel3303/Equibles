namespace Equibles.Sec.HostedService.Configuration;

/// <summary>
/// Controls the historical sweep that stamps <c>Document.Items</c> onto 8-K rows ingested
/// before item capture went live. Forward capture needs no switch — it reads the item list
/// from the submissions feed already fetched for ingest — so this only governs the backfill.
/// </summary>
public class FilingItemsBackfillOptions
{
    /// <summary>
    /// Off by default — the sweep is a one-time operation that re-fetches each pending
    /// company's submissions feed (plus its archive pages) and so spends the shared EDGAR
    /// budget; opt in via <c>FilingItemsBackfill__Enabled=true</c>.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// How many companies are swept per cycle. Each company costs one submissions-feed
    /// fetch plus its archive pages, and stamps every one of its pending 8-Ks in one go.
    /// </summary>
    public int BatchSize { get; set; } = 8;
}
