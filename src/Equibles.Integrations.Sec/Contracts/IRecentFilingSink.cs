using Equibles.Integrations.Sec.Models;

namespace Equibles.Integrations.Sec.Contracts;

/// <summary>
/// Optional receiver of EDGAR's latest-filings feed. The feed poller offers every registered
/// sink the entries it reads for the first time, of any form and filer, minutes after SEC
/// accepts them. The feed is a rolling window that can scroll entries out unseen, so a sink
/// treats an offer as early notice and keeps its own complete source.
/// </summary>
public interface IRecentFilingSink
{
    Task Offer(IReadOnlyList<EdgarRecentFilingEntry> entries, CancellationToken cancellationToken);
}
