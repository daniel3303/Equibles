using Equibles.Core.AutoWiring;
using Equibles.Integrations.Common.Http;
using Equibles.Integrations.XbrlFilings;
using Equibles.Integrations.XbrlFilings.Models;
using Equibles.Sec.BusinessLogic;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Retrieval text for a European report captured as JSON that carries no notes, read from the visual report
/// the same index record states. Reports before 2022 tag no notes, so their prose is only in the visual
/// report; the JSON stays the source of facts. An empty result settles the report empty by design.
/// </summary>
[Service]
public class EsefVisualReportText(
    XbrlFilingsClient client,
    ISecDocumentHtmlNormalizer normalizer,
    ISecDocumentHtmlToMarkdownConverter converter,
    ILogger<EsefVisualReportText> logger
)
{
    private const int IndexPageSize = 100;

    // An issuer files a handful of reports a year, so five pages is past any real issuer; an index that
    // states more is refused rather than read without end.
    internal const int MaxIndexPages = 5;

    /// <summary>
    /// Finds the index record whose JSON address is the stored one, exactly one, and reads its visual
    /// report. Two records for one address is a fault, never a choice.
    /// </summary>
    public async Task<byte[]> FromJsonAddress(
        string issuerLei,
        DateOnly periodEnd,
        string jsonAddress,
        CancellationToken cancellationToken
    )
    {
        var matches = new List<XbrlFiling>();
        for (var page = 1; ; page++)
        {
            if (page > MaxIndexPages)
                throw new InvalidDataException(
                    "The filing index states more filings for the issuer than it reads."
                );
            var filings = await client.GetFilingsOf(
                issuerLei,
                page,
                IndexPageSize,
                cancellationToken
            );
            matches.AddRange(
                filings.Filings.Where(filing =>
                    string.Equals(filing.JsonUrl?.ToString(), jsonAddress, StringComparison.Ordinal)
                )
            );
            if (filings.Filings.Count == 0 || page * IndexPageSize >= filings.TotalCount)
                break;
        }
        if (matches.Count > 1)
            throw new InvalidDataException(
                "The filing index states more than one record for the report's JSON address."
            );
        var reportAddress = matches.SingleOrDefault()?.ReportUrl;
        if (reportAddress == null || reportAddress.ToString() == jsonAddress)
        {
            logger.LogInformation(
                "The filing index states no visual report beside {JsonAddress}; it stays without text.",
                jsonAddress
            );
            return [];
        }
        return await FromReportAddress(issuerLei, periodEnd, reportAddress, cancellationToken);
    }

    /// <summary>
    /// Reads the visual report at an address the index stated, under the client's own ceiling, and builds
    /// its text the way an inline report's is built. A body naming another issuer or period is refused.
    /// </summary>
    public async Task<byte[]> FromReportAddress(
        string issuerLei,
        DateOnly periodEnd,
        Uri reportAddress,
        CancellationToken cancellationToken
    )
    {
        string xhtml;
        try
        {
            var payload = await client.GetReport(
                reportAddress,
                XbrlFilingsClient.MaxReportBytes,
                cancellationToken
            );
            xhtml = SameOriginTextReader.Decode(payload.CharSet, payload.Bytes);
        }
        // The ceiling is a property of the report, not a fault, so the report settles empty rather than
        // being fetched again on every retry.
        catch (SameOriginSizeException)
        {
            logger.LogInformation(
                "The visual report {ReportAddress} is past the {Limit}-byte ceiling; it stays without text.",
                reportAddress,
                XbrlFilingsClient.MaxReportBytes
            );
            return [];
        }
        switch (EsefVisualReport.ConfirmsIdentity(xhtml, issuerLei, periodEnd))
        {
            case null:
                logger.LogInformation(
                    "The visual report {ReportAddress} is not XHTML an XML reader accepts; it stays without text.",
                    reportAddress
                );
                return [];
            case false:
                throw new InvalidDataException(
                    "The visual report names another issuer or period than its JSON report."
                );
        }
        return EsefReportContent.Build(xhtml, normalizer, converter);
    }
}
