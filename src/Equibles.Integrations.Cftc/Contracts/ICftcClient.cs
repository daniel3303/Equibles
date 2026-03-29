using Equibles.Integrations.Cftc.Models;

namespace Equibles.Integrations.Cftc.Contracts;

public interface ICftcClient {
    Task<List<CftcReportRecord>> DownloadYearlyReport(int year);
}
