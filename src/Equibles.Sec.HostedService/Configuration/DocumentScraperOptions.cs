using Equibles.Sec.Data.Models;

namespace Equibles.Sec.HostedService.Configuration;

public class DocumentScraperOptions
{
    // Amendments are first-class: 10-K/A, 10-Q/A and 8-K/A store as their own
    // document types, and Form 4/A / 3/A supersede their originals' transactions
    // in the insider pipeline. Omitting them left corrected filings invisible
    // while the erroneous originals stayed live.
    //
    // 20-F/6-K/40-F are a foreign private issuer's equivalents of 10-K/8-K (40-F
    // specifically for Canadian filers using the MJDS annual-report regime) —
    // already fully wired end to end (SEC filter mapping, form-name detection,
    // HTML/XBRL/PDF-fallback extraction, even used elsewhere to infer fiscal
    // year-end when no 10-K exists), but missing from this list meant a foreign
    // filer like OceanaGold (ticker OGC, CIK 0001487326, files 6-K routinely and
    // 40-F annually) synced as a known company with zero documents ever ingested.
    public List<DocumentType> DocumentTypesToSync { get; set; } =
    [
        DocumentType.TenK,
        DocumentType.TenQ,
        DocumentType.EightK,
        DocumentType.TenKa,
        DocumentType.TenQa,
        DocumentType.EightKa,
        DocumentType.TwentyF,
        DocumentType.SixK,
        DocumentType.FortyF,
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
    public int RecentFeedPollSeconds { get; set; } = 10;

    // Max ATOM pages (100 entries each) walked per poll when every entry is
    // still unseen (first poll after a boot, or a heavy burst).
    public int RecentFeedMaxPages { get; set; } = 5;

    // A feed-flagged filing can be invisible to the company's submissions JSON
    // for minutes — the JSON is served through a CDN that lags acceptance, so a
    // company enumerated seconds after its feed flag legitimately finds nothing
    // and would otherwise drop the filing until the daily-index backstop next
    // morning. Such accessions stay pending and re-dirty their company until
    // the enumeration sees them: at most one retry per this many seconds …
    public int FeedPendingRetrySeconds { get; set; } = 120;

    // … at most this many retries per filing (past a handful the JSON is not
    // merely lagging — abandon with a warning; the daily index owns recovery) …
    public int FeedPendingMaxRetries { get; set; } = 10;

    // … abandoned regardless after this many minutes of wall clock (covers
    // entries that never got an enumeration attempt at all) …
    public int FeedPendingExpiryMinutes { get; set; } = 360;

    // … and at most this many pending re-flags admitted per cycle (oldest
    // first), so a submissions-JSON stall cannot turn every recently flagged
    // filer into a simultaneous re-enumeration storm — the same bounding idea
    // as MaxReconciliationsPerCycle.
    public int MaxPendingReflagsPerCycle { get; set; } = 50;

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
