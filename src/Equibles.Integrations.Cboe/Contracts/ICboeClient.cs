using Equibles.Integrations.Cboe.Models;

namespace Equibles.Integrations.Cboe.Contracts;

public interface ICboeClient
{
    Task<Dictionary<CboePutCallProductType, CboePutCallRecord>> DownloadDailyPutCallRatios(
        DateOnly date
    );
    Task<List<CboeVixRecord>> DownloadVixHistory();
}
