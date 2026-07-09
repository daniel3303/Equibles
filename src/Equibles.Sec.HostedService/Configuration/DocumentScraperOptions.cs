using Equibles.Sec.Data.Models;

namespace Equibles.Sec.HostedService.Configuration;

public class DocumentScraperOptions
{
    // Amendments are first-class: 10-K/A, 10-Q/A and 8-K/A store as their own
    // document types, and Form 4/A / 3/A supersede their originals' transactions
    // in the insider pipeline. Omitting them left corrected filings invisible
    // while the erroneous originals stayed live.
    public List<DocumentType> DocumentTypesToSync { get; set; } =
    [
        DocumentType.TenK,
        DocumentType.TenQ,
        DocumentType.EightK,
        DocumentType.TenKa,
        DocumentType.TenQa,
        DocumentType.EightKa,
        DocumentType.FormFour,
        DocumentType.FormThree,
        DocumentType.FormFourA,
        DocumentType.FormThreeA,
        DocumentType.Form144,
        DocumentType.FormD,
        DocumentType.FormDa,
        DocumentType.NCen,
        DocumentType.NCenA,
        DocumentType.NportP,
        DocumentType.NportPa,
        DocumentType.Def14A,
    ];

    // Event-driven discovery replaces the legacy sweep that re-fetched every
    // company's submissions JSON every cycle (>95% of those polls found nothing;
    // they consumed a third of the shared EDGAR request budget). Kill switch:
    // false restores the legacy full sweep with no code change.
    public bool UseEventDrivenDiscovery { get; set; } = true;

    // Minimum seconds between "Latest Filings" ATOM feed polls. The feed holds
    // ~100 entries per page and peak dissemination bursts run tens of filings a
    // minute, so the poll interval bounds the realtime layer's blind window.
    public int RecentFeedPollSeconds { get; set; } = 60;

    // Max ATOM pages (100 entries each) walked per poll when every entry is
    // still unseen (first poll after a boot, or a heavy burst).
    public int RecentFeedMaxPages { get; set; } = 5;

    // A company whose last full filing enumeration is older than this gets a
    // reconciliation re-sweep — the correctness backstop that converges on the
    // authoritative submissions JSON no matter what the realtime layers missed.
    public int ReconciliationHours { get; set; } = 24;

    // Cap on reconciliation re-sweeps per cycle so a cold start (no stamps yet)
    // drains as a rolling backfill instead of one monster cycle.
    public int MaxReconciliationsPerCycle { get; set; } = 400;

    // Max daily-index days processed per cycle when catching up after downtime.
    public int DailyIndexMaxDaysPerCycle { get; set; } = 7;

    // Minimum minutes between SEC company-directory syncs in event-driven mode.
    // The legacy sweep synced once per multi-hour cycle; event-driven cycles run
    // every few seconds, so an unthrottled sync would hammer company_tickers.
    public int CompanySyncIntervalMinutes { get; set; } = 60;
}
