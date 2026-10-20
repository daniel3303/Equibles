using Equibles.Core.AutoWiring;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Microsoft.Extensions.Logging;

namespace Equibles.InsiderTrading.BusinessLogic;

/// <summary>
/// Reads the trading symbol an issuer's newest ownership filing (Form 3, 4 or 5) states for it.
/// The ownership document names the issuer's CIK and symbol in one block, so it is SEC-filed
/// evidence that the symbol was that CIK's; a filing in the feed for another issuer proves nothing.
/// </summary>
[Service]
public class IssuerTradingSymbolWitness
{
    // Insider filings cluster; the newest few always include one about the issuer itself unless
    // the CIK only ever reported as an owner.
    private const int MaxDocuments = 3;

    private static readonly HashSet<string> OwnershipForms = new(StringComparer.Ordinal)
    {
        "3",
        "3/A",
        "4",
        "4/A",
        "5",
        "5/A",
    };

    private readonly ISecEdgarClient _secEdgarClient;
    private readonly ILogger<IssuerTradingSymbolWitness> _logger;

    public IssuerTradingSymbolWitness(
        ISecEdgarClient secEdgarClient,
        ILogger<IssuerTradingSymbolWitness> logger
    )
    {
        _secEdgarClient = secEdgarClient;
        _logger = logger;
    }

    /// <summary>
    /// The symbol from the newest ownership filing about <paramref name="cik"/> filed on or after
    /// <paramref name="filedOnOrAfter"/>, or null when none states one or EDGAR cannot be read.
    /// </summary>
    public async Task<string> GetNewestStatedSymbol(
        string cik,
        DateOnly filedOnOrAfter,
        CancellationToken cancellationToken = default
    )
    {
        List<FilingData> filings;
        try
        {
            filings = await _secEdgarClient.GetCompanyFilings(cik, fromDate: filedOnOrAfter);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Could not list ownership filings for CIK {Cik}", cik);
            return null;
        }

        var newest = filings
            .Where(filing => OwnershipForms.Contains(filing.Form))
            .OrderByDescending(filing => filing.FilingDate)
            .ThenByDescending(filing => filing.AccessionNumber, StringComparer.Ordinal)
            .Take(MaxDocuments);
        foreach (var filing in newest)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string content;
            try
            {
                content = await _secEdgarClient.GetDocumentContent(
                    filing.AccessionNumber,
                    filing.Cik,
                    cancellationToken
                );
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(
                    exception,
                    "Could not read ownership filing {Accession} for CIK {Cik}",
                    filing.AccessionNumber,
                    cik
                );
                continue;
            }

            var symbol = InsiderFilingParser.GetIssuerTradingSymbol(content, cik);
            if (symbol != null)
                return symbol;
        }

        return null;
    }
}
