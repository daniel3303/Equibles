using Equibles.Integrations.Cboe.Models;

namespace Equibles.Integrations.Cboe.Contracts;

public interface ICboeClient {
    Task<List<CboePutCallRecord>> DownloadPutCallRatios(CboePutCallCsvType csvType);
    Task<List<CboeVixRecord>> DownloadVixHistory();
}
