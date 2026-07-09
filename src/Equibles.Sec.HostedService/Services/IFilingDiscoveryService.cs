using Equibles.CommonStocks.Data.Models;

namespace Equibles.Sec.HostedService.Services;

public interface IFilingDiscoveryService
{
    /// <summary>
    /// Returns the tracked companies that likely have new filings since the
    /// last cycle, discovered from EDGAR's centralized feeds: the real-time
    /// "Latest Filings" ATOM stream (minutes of latency, lossy under bursts)
    /// plus the immutable per-day master index (complete, hours of latency,
    /// watermarked so downtime is caught up without loss). Both layers are
    /// best-effort — the periodic per-company reconciliation sweep is the
    /// correctness backstop for anything they miss.
    /// </summary>
    Task<List<CommonStock>> DiscoverCompaniesWithNewFilings(
        IReadOnlyList<CommonStock> trackedCompanies,
        CancellationToken cancellationToken = default
    );
}
