using System.Text;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.BusinessLogic;
using Equibles.Sec.HostedService.Configuration;
using Equibles.Sec.HostedService.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Examines a filing's already-fetched submission envelope for its raw XBRL and decides
/// what (if anything) the persistence layer should store on the <c>Document</c>. Capture is
/// opt-in (see <see cref="XbrlCaptureOptions"/>), reads only the in-hand submission so it
/// costs no extra EDGAR round-trip, and is best-effort — any failure leaves the document
/// <c>NotChecked</c> so a later backfill can retry it, never breaking ingest.
/// </summary>
public class XbrlEnvelopeCaptureService
{
    private readonly XbrlCaptureOptions _options;
    private readonly ILogger<XbrlEnvelopeCaptureService> _logger;

    public XbrlEnvelopeCaptureService(
        IOptions<XbrlCaptureOptions> options,
        ILogger<XbrlEnvelopeCaptureService> logger
    )
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Resolves the XBRL capture outcome for a filing from its full submission content
    /// (the <c>{accession}.txt</c> already downloaded for ingest).
    /// </summary>
    public XbrlCaptureResult Capture(string submissionContent, FilingData filing)
    {
        if (!_options.Enabled)
        {
            return XbrlCaptureResult.NotChecked;
        }

        try
        {
            if (
                SecDocumentEnvelopeParser.TryExtractXbrlEnvelope(
                    submissionContent,
                    filing.PrimaryDocument,
                    out var type,
                    out var sourceFileName,
                    out var body
                )
            )
            {
                return XbrlCaptureResult.Captured(
                    type,
                    sourceFileName,
                    Encoding.UTF8.GetBytes(body)
                );
            }

            return XbrlCaptureResult.NotPresent;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to extract XBRL envelope for {Accession}; left unchecked for backfill.",
                filing.AccessionNumber
            );
            return XbrlCaptureResult.NotChecked;
        }
    }
}
