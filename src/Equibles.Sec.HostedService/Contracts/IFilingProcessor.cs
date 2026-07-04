using Equibles.CommonStocks.Data.Models;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.Data.Models;

namespace Equibles.Sec.HostedService.Contracts;

/// <summary>
/// Strategy interface for specialized processing of SEC filings.
/// Implementations handle specific document types (e.g., Form 3/4 → structured insider trading records)
/// instead of the default HTML→Markdown→Document pipeline.
/// </summary>
public interface IFilingProcessor
{
    bool CanProcess(DocumentType documentType);
    Task<bool> Process(FilingData filing, CommonStock company);

    /// <summary>
    /// The subset of <paramref name="accessionNumbers"/> this processor has already
    /// ingested, resolved in ONE batched query. The scraper prefilters a company's
    /// filing list through this so re-enumerated history costs one round-trip per
    /// (company, type) pass instead of one DB query and DI scope per filing.
    /// </summary>
    Task<HashSet<string>> FilterKnownAccessions(IReadOnlyCollection<string> accessionNumbers);
}
