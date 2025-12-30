using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Integrations.Sec.Models;

namespace Equibles.Sec.HostedService.Contracts;

/// <summary>
/// Strategy interface for specialized processing of SEC filings.
/// Implementations handle specific document types (e.g., Form 3/4 → structured insider trading records)
/// instead of the default HTML→Markdown→Document pipeline.
/// </summary>
public interface IFilingProcessor {
    bool CanProcess(DocumentType documentType);
    Task<bool> Process(FilingData filing, CommonStock company);
}
