using Equibles.CommonStocks.Data.Models;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.Data.Models;

namespace Equibles.Sec.HostedService.Models;

public record DeferredFiling(EquityIssuer Company, FilingData Filing, DocumentType DocumentType);
