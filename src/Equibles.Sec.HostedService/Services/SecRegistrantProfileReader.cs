using Equibles.CommonStocks.BusinessLogic;
using Equibles.CommonStocks.Data.Models;
using Equibles.Core.AutoWiring;
using Equibles.Integrations.Sec;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Persists the registrant facts EDGAR's header and filing index state: incorporation and
/// business-address codes, the periodic-report file number, and any business development
/// company election or withdrawal. The recent list comes with the submissions header at no
/// extra cost. A filer EDGAR never classified by industry (no SIC), whose recent list shows no
/// election, has its archived pages read once for the election forms; SIC only chooses where
/// to look, the filing alone decides. Until those pages are requested the profile is stored
/// without its read date, so it stays due; a failed request still dates the read, so a broken
/// archive cannot lead the refresh queue every cycle, and the filing scraper retries it.
/// </summary>
[Service]
public class SecRegistrantProfileReader(
    ISecEdgarClient secEdgarClient,
    EquityIdentityManager identityManager,
    ILogger<SecRegistrantProfileReader> logger
)
{
    /// <summary>Stores the profile from <paramref name="metadata"/>.</summary>
    /// <returns>Whether the archived filing pages were requested.</returns>
    public async Task<bool> Store(
        EquityIssuer company,
        CompanyMetadata metadata,
        bool mayReadArchive
    )
    {
        var election = metadata.BdcElectionFiledOn;
        var withdrawal = metadata.BdcWithdrawalFiledOn;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        // The history date is recorded once: rewriting it every read would save every filer daily.
        DateOnly? historyReadOn =
            metadata.HasArchivedFilings || company.BdcHistoryReadOn != null ? null : today;
        var archiveDue =
            metadata.HasArchivedFilings
            && election == null
            && company.BdcHistoryReadOn == null
            && string.IsNullOrWhiteSpace(metadata.Sic);
        var archiveRequested = false;

        if (archiveDue && mayReadArchive)
        {
            archiveRequested = true;
            archiveDue = false;
            try
            {
                var history = await secEdgarClient.GetCompanyFilings(company.Cik);
                election = SecRegistrantFilings.LatestBdcElection(history);
                withdrawal = SecRegistrantFilings.LatestBdcWithdrawal(history);
                historyReadOn = today;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Could not read the archived filing history of {Ticker} (CIK: {Cik}) for business development company elections",
                    (company.Presentation?.Listing?.Ticker ?? company.Cik),
                    company.Cik
                );
            }
        }

        await identityManager.SetSecRegistrantProfile(
            company,
            metadata.StateOfIncorporation,
            metadata.BusinessStateOrCountry,
            metadata.PeriodicReportFileNumber,
            election,
            withdrawal,
            historyReadOn,
            archiveDue ? null : today
        );
        return archiveRequested;
    }
}
