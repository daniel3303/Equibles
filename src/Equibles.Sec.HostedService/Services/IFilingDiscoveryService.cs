using Equibles.CommonStocks.Data.Models;

namespace Equibles.Sec.HostedService.Services;

public interface IFilingDiscoveryService
{
    /// <summary>
    /// Returns the tracked companies that likely have new filings since the
    /// last cycle, discovered from EDGAR's centralized feeds: the real-time
    /// "Latest Filings" ATOM stream (a configurable interval with a ten-second
    /// default minimum, lossy under bursts)
    /// plus the immutable per-day master index (complete, hours of latency,
    /// watermarked so downtime is caught up without loss). Both layers are
    /// best-effort — the periodic per-company reconciliation sweep is the
    /// correctness backstop for anything they miss.
    /// </summary>
    Task<List<CommonStock>> DiscoverCompaniesWithNewFilings(
        IReadOnlyList<CommonStock> trackedCompanies,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// True while any feed-flagged filing awaits confirmation by a company
    /// enumeration — the scraper only collects enumerated accessions for
    /// <see cref="MarkAccessionsEnumerated"/> when this is set.
    /// </summary>
    bool HasPendingFeedAccessions { get; }

    /// <summary>
    /// Reports the (accession, filer CIK) pairs a company enumeration actually
    /// returned, confirming any feed-flagged filings still pending under that
    /// CIK. A filing the feed flagged but the (lagging) submissions JSON hasn't
    /// listed yet stays pending and keeps its company in the discovery set
    /// until confirmed here or abandoned (retry/expiry bounds).
    /// </summary>
    void MarkAccessionsEnumerated(
        IReadOnlyCollection<(string AccessionNumber, string Cik)> enumeratedFilings
    );
}
