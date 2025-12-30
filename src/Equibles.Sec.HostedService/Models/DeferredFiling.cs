using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Integrations.Sec.Models;

namespace Equibles.Sec.HostedService.Models;

public record DeferredFiling(CommonStock Company, FilingData Filing, DocumentType DocumentType);
