using System.Text.RegularExpressions;
using System.Xml.Linq;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Integrations.Sec.Models;

namespace Equibles.Sec.HostedService.Helpers;

/// <summary>
/// Shared parsing for SEC issuer-feed XML submissions (Form 144, Form D, N-CEN, NPORT-P).
/// Each of these forms wraps its payload in the same SGML envelope and uses the
/// <c>edgarSubmission</c> root, so envelope stripping, ampersand escaping, and the
/// parse/skip/error-report flow are identical across their processors.
/// </summary>
internal static class EdgarXmlSubmissionParser
{
    private const string XmlEnvelopeStart = "<XML>";
    private const string XmlEnvelopeEnd = "</XML>";

    /// <summary>
    /// Strips the SGML envelope and escapes stray ampersands so the payload parses as XML.
    /// </summary>
    internal static string SanitizeXml(string xml)
    {
        // SEC filings wrap the actual XML inside an SGML envelope (one .txt holds the whole
        // submission); pull out the <XML>...</XML> body before parsing.
        var xmlStart = xml.IndexOf(XmlEnvelopeStart, StringComparison.OrdinalIgnoreCase);
        var xmlEnd = xml.IndexOf(XmlEnvelopeEnd, StringComparison.OrdinalIgnoreCase);
        if (xmlStart >= 0 && xmlEnd > xmlStart)
        {
            xml = xml[(xmlStart + XmlEnvelopeStart.Length)..xmlEnd].Trim();
        }

        // Escape stray ampersands that aren't already part of an entity.
        return Regex.Replace(xml, @"&(?!(amp|lt|gt|quot|apos|#\d+|#x[\da-fA-F]+);)", "&amp;");
    }

    /// <summary>
    /// Sanitizes the raw submission and returns its <c>edgarSubmission</c> root, or
    /// <c>null</c> when the filing isn't XML or is malformed (logging and reporting the case).
    /// </summary>
    /// <param name="filingTypeName">Human-readable form label used in log messages (e.g. "Form 144").</param>
    /// <param name="errorContext">Error-reporter context label (e.g. "Form144.ParseXml").</param>
    internal static async Task<XElement> TryParseSubmission(
        string content,
        FilingData filing,
        string companyTicker,
        string filingTypeName,
        string errorContext,
        ILogger logger,
        ErrorReporter errorReporter
    )
    {
        var sanitized = SanitizeXml(content);

        // These forms are XML-only, so a submission without the <edgarSubmission> root is
        // unexpected — skip quietly.
        if (!sanitized.Contains("<edgarSubmission", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogDebug(
                "Skipping non-XML {FilingType} filing for {Ticker} - {AccessionNumber}",
                filingTypeName,
                companyTicker,
                filing.AccessionNumber
            );
            return null;
        }

        try
        {
            return XDocument.Parse(sanitized).Root;
        }
        catch (System.Xml.XmlException ex)
        {
            logger.LogWarning(
                ex,
                "Malformed {FilingType} XML for {Ticker} - {AccessionNumber}",
                filingTypeName,
                companyTicker,
                filing.AccessionNumber
            );
            await errorReporter.Report(
                ErrorSource.DocumentScraper,
                errorContext,
                ex.Message,
                ex.StackTrace,
                $"ticker: {companyTicker}, accession: {filing.AccessionNumber}"
            );
            return null;
        }
    }
}
